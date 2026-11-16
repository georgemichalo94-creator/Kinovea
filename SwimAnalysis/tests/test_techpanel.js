// 2.18.0: the panel of the start and the turn in the technique report: the picture of Kinovea, big, on the first page after the numbers;
// the evaluation from the top of the next page; without a comparison no speed chart (the panel has the speed of each segment), with a
// comparison the speed chart after the evaluation (it shows the previous one and elite); without a panel as before (the speed chart
// before the evaluation). All names and values fictional.
const { makeContext } = require('./mock_gas');
const assert = require('assert');

const G = makeContext({ athletes: [['unique_id', 'ΟΝΟΜΑΤΕΠΩΝΥΜΟ', 'ΦΥΛΟ'], [500201, 'ΦΑΝΤΑΣΤΙΚΟΣ ΠΙΝΑΚΑΣ', 'Ανδρών']] });
G.swimShowConnectionInfo = () => {};
G.swimSetup();
const call = body => JSON.parse(G.swimDoPost({ postData: { contents: JSON.stringify(Object.assign({ token: 'tok', app: 'kinovea-swim' }, body)) } }).text);
const reports = G._all[Object.keys(G._all).find(k => k !== 'DATA')];
const sheetOf = r => reports.getSheets().find(s => s.getSheetId() === r.sheetId);
const texts = sh => Object.values(sh._cells).map(c => c.v).filter(v => typeof v === 'string');

// The pictures of Kinovea (PNG) in the folder of the snapshots.
const png = Buffer.from([137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3, 4]).toString('base64');
const up = (raceId, id) => {
  const r = call({ action: 'image', raceId, snapshotId: id, mime: 'image/png', data: png });
  assert.ok(r.ok && r.fileId, JSON.stringify(r));
  return r.fileId;
};

const sheet = { title: 'Φανταστικό φύλλο στροφής', source: 'Δοκιμή', scored: true, groups: [
  { label: 'Προσέγγιση', items: [{ n: '1', key: 'p1', text: 'Πρώτο σημείο' }, { n: '2', key: 'p2', text: 'Δεύτερο σημείο', cue: 'Διόρθωση δύο' }] },
  { label: 'Ώθηση', items: [{ n: '3', key: 'p3', text: 'Τρίτο σημείο' }, { n: '4', key: 'p4', text: 'Τέταρτο σημείο' }] }] };
const turnValues = k => ({ in5: 3.12 + k, lastStrokeGap: 1.28, rotation: 0.91 + k, contact: 0.24, turnTime: 1.52 + k,
  out5: 2.18 + k, out10: 4.95 + k, out15: 7.85 + k, inOut5: 5.30 + 2 * k, inOut10: 8.07 + 2 * k, inOut15: 10.97 + 2 * k,
  v0_5: 2.29, v5_10: 1.81, v10_15: 1.72, underwater: 3.71, kicks: 6, kickRate: 2.10, uwVel: 1.73, breakoutTime: 3.95,
  breakoutDist: 8.4, bo15Vel: 1.69, firstStroke: 4.30 });
const race = (id, date, opts) => {
  opts = opts || {};
  const phases = [{ id: id + '-t1', kind: 'turn', attempt: 1, values: turnValues(0) }];
  if (opts.two) phases.push({ id: id + '-t2', kind: 'turn', attempt: 2, values: turnValues(0.05) });
  return {
    id, kind: 'training', athlete: 'ΦΑΝΤΑΣΤΙΚΟΣ ΠΙΝΑΚΑΣ', athleteId: '500201', gender: 'Ανδρών', style: 'fs', pool: 25, date, checkpoints: [],
    graphics: opts.panel ? [{ id: 'panel-turn', fileId: opts.panel }] : [],
    tech: { phases,
      snapshots: [{ id: id + '-s1', t: 0, phase: id + '-t1', stage: 'Επαφή', event: 'Επαφή στον τοίχο', comment: '', fileId: opts.snap || '' }],
      values: { notes: opts.notes ? 'Σημειώσεις ' + id : '' }, sheets: opts.noEval ? {} : { 'tp-turn': sheet },
      evals: opts.noEval ? [] : [{ id: id + '-ev', phase: id + '-t1', phaseLabel: 'Στροφή 1', phaseKind: 'turn', sheet: 'tp-turn', date,
        scores: { p1: { v: 1 }, p2: { v: 0 }, p3: { v: 1 }, p4: { v: 1 } } }] }
  };
};
const layoutOf = (r, cmp) => {
  const rr = G.swimNormalizeRace_(r);
  const extras = { pageHeight: w => G.swimPdfPageHeight_(reports, w) };
  extras.techCompare = cmp !== undefined ? cmp : G.swimTechCompare_(rr);
  return G.swimBuildLayout_(rr, G.swimAnalyze_(rr), extras);
};
const rowOf = (built, test) => {
  const cells = Object.values(built.grid.cells).filter(c => typeof c.v === 'string' && (test instanceof RegExp ? test.test(c.v) : c.v === test));
  return cells.length ? Math.min(...cells.map(c => c.r)) : -1;
};
// The page of a row and where it begins on it, as the PDF puts them (the heights of the rows of the sheet).
const pageOf = (built, row) => {
  let page = 1, y = 0;
  for (let r = 1; ; r++) {
    const h = G.swimRowHeight_(built.grid, r);
    if (y > 0 && y + h > built.page.height) { page++; y = 0; }
    if (r === row) return { page, y };
    y += h;
  }
};
const speedCharts = built => built.charts.filter(c => /ταχύτητα ανά τμήμα|speed per segment/i.test(c.title || ''));

// ---- 1) The race keeps the panels of a technique analysis (and nothing else).
const panelId = up('tpA', 'report-panel-turn');
const startId = up('tpA', 'report-panel-start');
const norm = G.swimNormalizeRace_(Object.assign(race('tpA', '16.11.2026'), { graphics: [{ id: 'panel-turn', fileId: panelId },
  { id: 'panel-start', fileId: startId }, { id: 'panel-other', fileId: panelId }, { id: 'panel-turn', fileId: 'not an id' }] }));
assert.deepStrictEqual(JSON.parse(JSON.stringify(norm.graphics)), { 'panel-turn': panelId, 'panel-start': startId });

// ---- 2) With the panel, without a comparison: the panel after all the numbers, on the first page; the evaluation from the top of the
// next page; no speed chart.
let b = layoutOf(race('tpA', '16.11.2026', { panel: panelId, notes: true }), null);
const numbers = rowOf(b, 'Όλα τα νούμερα της στροφής');
const panelTitle = rowOf(b, 'Ο πίνακας της στροφής (Kinovea)');
const evalTitle = rowOf(b, 'Αξιολόγηση τεχνικής');
assert.ok(numbers > 0 && panelTitle > numbers && evalTitle > panelTitle, 'order: ' + [numbers, panelTitle, evalTitle]);
const im = b.images.find(x => x.fileId === panelId);
assert.ok(im, 'the picture of the panel: ' + JSON.stringify(b.images));
assert.strictEqual(im.row, panelTitle + 1, 'the picture under its title');
assert.ok(im.width === 1116 && im.height === 628 && im.x >= 0 && im.x + im.width <= 1124, 'big, across the page: ' + JSON.stringify(im));
assert.strictEqual(pageOf(b, numbers).page, 1, 'the numbers on the first page');
const imBottom = (() => { let y = 0, r = im.row; while (y < im.height + 4) { y += G.swimRowHeight_(b.grid, r); r++; } return r - 1; })();
assert.strictEqual(pageOf(b, im.row).page, 1, 'the panel on the first page');
assert.strictEqual(pageOf(b, imBottom).page, 1, 'the panel whole on the first page');
const ev = pageOf(b, evalTitle);
assert.ok(ev.page === 2 && ev.y === 0, 'the evaluation at the top of page 2: ' + JSON.stringify(ev));
assert.strictEqual(speedCharts(b).length, 0, 'no speed chart with the panel: ' + JSON.stringify(b.charts.map(c => c.title)));
assert.ok(rowOf(b, 'Σημειώσεις') > evalTitle, 'the notes after the evaluation');
const withPanel = b;

// ---- 3) Without the panel: as before, the speed chart before the evaluation (and the evaluation still from a new page).
b = layoutOf(race('tpA', '16.11.2026', { notes: true }), null);
assert.strictEqual(rowOf(b, 'Ο πίνακας της στροφής (Kinovea)'), -1, 'no title of a panel without its picture');
assert.strictEqual(speedCharts(b).length, 1, 'the speed chart without the panel');
assert.ok(speedCharts(b)[0].row < rowOf(b, 'Αξιολόγηση τεχνικής'), 'before the evaluation, as before');
assert.strictEqual(pageOf(b, rowOf(b, 'Αξιολόγηση τεχνικής')).y, 0, 'the evaluation from the top of a page');
assert.notStrictEqual(withPanel.signature, b.signature, 'the panel coming places the report again');

// ---- 4) Two attempts: the panel of the best one, said in its title.
b = layoutOf(race('tpA', '16.11.2026', { panel: panelId, two: true }), null);
assert.ok(rowOf(b, 'Ο πίνακας της στροφής (Kinovea) · η καλύτερη από 2 στροφές') > 0, 'the best of two');

// ---- 5) A new page: the rest of the page filled, nothing at the top of a page or in the first pass; without an evaluation none.
{
  const grid = new G.SwimGrid_();
  for (let r = 1; r <= 10; r++) grid.rowHeights[r] = 100;
  const L = { g: grid, paging: G.swimNewPaging_(1000, {}) };
  const y5 = G.swimPageTop_(grid, 5, 1000);
  assert.ok(y5 > 0, 'row 5 inside the page');
  const next = G.swimNewPage_(L, 5);
  assert.ok(next > 5 && G.swimPageTop_(grid, next, 1000) + G.swimRowHeight_(grid, next) > 1000, 'the next row does not fit: a new page');
  let filled = 0;
  for (let r = 5; r < next; r++) filled += grid.exactHeights[r];
  assert.ok(Math.abs(y5 + filled - 998) <= 1, 'the rest of the page filled: ' + (y5 + filled));
  const top = { g: new G.SwimGrid_(), paging: G.swimNewPaging_(1000, {}) };
  assert.strictEqual(G.swimNewPage_(top, 1), 1, 'at the top of a page: nothing');
  assert.strictEqual(G.swimNewPage_({ g: new G.SwimGrid_(), paging: G.swimNewPaging_(0, null) }, 7), 7, 'the first pass: nothing');
  const noEval = layoutOf(race('tpA', '16.11.2026', { panel: panelId, noEval: true }), null);
  assert.strictEqual(rowOf(noEval, 'Αξιολόγηση τεχνικής'), -1, 'no evaluation');
  assert.ok(noEval.images.some(x => x.fileId === panelId), 'the panel without an evaluation');
}

// ---- 6) With a comparison (a previous analysis): the speed chart stays, after the evaluation.
let r = call({ action: 'update', race: race('tpP', '10.11.2026') });
assert.ok(r.ok, JSON.stringify(r));
b = layoutOf(Object.assign(race('tpB', '16.11.2026', { panel: panelId }), { compare: { techPrev: 'tpP' } }));
assert.ok(b.grid && rowOf(b, /Σύγκριση/) > 0, 'compared with the previous analysis');
assert.ok(b.images.some(x => x.fileId === panelId), 'the panel with the comparison');
assert.strictEqual(speedCharts(b).length, 1, 'the speed chart with a comparison');
assert.ok(speedCharts(b)[0].row > rowOf(b, 'Αξιολόγηση τεχνικής'), 'after the evaluation');
assert.strictEqual(pageOf(b, rowOf(b, 'Αξιολόγηση τεχνικής')).y, 0, 'the evaluation from the top of a page (comparison)');

// ---- 7) The whole way: the sheet with the picture, the PDF with the evaluation on page 2.
const raceC = race('tpC', '16.11.2026', { panel: up('tpC', 'report-panel-turn') });
r = call({ action: 'update', race: raceC });
assert.ok(r.ok && r.rebuilt, JSON.stringify(r));
let sh = sheetOf(r);
const pics = sh._images.filter(x => x.blob && /_report-panel-turn\.png$/.test(x.blob._name));
assert.strictEqual(pics.length, 1, 'the panel in the sheet: ' + sh._images.map(x => x.blob && x.blob._name).join(', '));
const n0 = G._exports.length;
r = call({ action: 'pdf', race: raceC });
assert.ok(r.ok, JSON.stringify(r).slice(0, 300));
const ex = G._exports.slice(n0).filter(e => e.name === sh.getName()).pop();
assert.ok(ex, 'the PDF of the report');
const evalRowSheet = Object.keys(ex.detail.cells).filter(k => ex.detail.cells[k].v === 'Αξιολόγηση τεχνικής').map(k => Number(k.split(':')[0]))[0];
assert.ok(ex.detail.pageStarts.indexOf(evalRowSheet) >= 1, 'a page begins with the evaluation: ' + JSON.stringify([evalRowSheet, ex.detail.pageStarts]));
assert.ok(pics[0].row < ex.detail.pageStarts[1], 'the panel on the first page of the PDF');

// ---- 8) English.
r = call({ action: 'update', race: race('tpE', '16.11.2026', { panel: up('tpE', 'report-panel-turn'), two: true }), lang: 'en' });
assert.ok(r.ok, JSON.stringify(r));
sh = sheetOf(r);
assert.ok(texts(sh).includes('The panel of the turn (Kinovea) · the best of 2 turns'), 'English title: ' + texts(sh).filter(v => /panel/i.test(v)).join(' | '));

console.log('test_techpanel OK: the panel after the numbers on the first page, the evaluation from the next page, the speed chart only with a comparison');
