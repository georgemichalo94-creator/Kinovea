// 2.16.6: a faster report and PDF. When the report of a technique analysis is rebuilt (each change of its evaluation), the images
// of the layout that did not change stay in the sheet and move to their new place (not downloaded again from Drive), the ones no
// longer needed go, the boxes go in one call, and the answers say how long each part took (timings: report, PDF, KOE App).
// Fictional data only.
const { makeContext } = require('./mock_gas');
const assert = require('assert');

const G = makeContext({ athletes: [['unique_id', 'ΟΝΟΜΑΤΕΠΩΝΥΜΟ', 'ΦΥΛΟ'], [500002, 'ΦΑΝΤΑΣΤΙΚΟΣ ΚΟΛΥΜΒΗΤΗΣ', 'Ανδρών']] });
G.swimShowConnectionInfo = () => {};
G.swimSetup();
const call = body => JSON.parse(G.swimDoPost({ postData: { contents: JSON.stringify(Object.assign({ token: 'tok', app: 'kinovea-swim' }, body)) } }).text);
const reports = G._all[Object.keys(G._all).find(k => k !== 'DATA')];
const sheetOf = r => reports.getSheets().find(s => s.getSheetId() === r.sheetId);

const list = {
  title: 'Φανταστική λίστα στροφής', source: 'Δοκιμή', scored: true,
  groups: [{ label: 'Στροφή', items: [{ n: '1', key: 'x.one', text: 'Σημείο ένα', level: 'A' }, { n: '2', key: 'x.two', text: 'Σημείο δύο', level: 'P' },
    { n: '3', key: 'x.three', text: 'Σημείο τρία', level: 'X' }] }]
};
const phases = [{ id: 'tq1', kind: 'turn', attempt: 1, values: { in5: 2.6, turnTime: 0.8 } }];
const snap = (id, file, t) => ({ id, t, phase: 'tq1', stage: 'Στροφή', event: '', comment: 'Σχόλιο ' + id, fileId: file });
const race = (scores, snapshots) => ({
  id: 'trSpeed', kind: 'training', athlete: 'ΦΑΝΤΑΣΤΙΚΟΣ ΚΟΛΥΜΒΗΤΗΣ', athleteId: '500002', gender: 'Ανδρών', style: 'fs', pool: 50, date: '04.11.2026',
  checkpoints: [], tech: { phases, angles: [], snapshots, values: { notes: '' }, sheets: { 'speed-turn': list },
    evals: [{ id: 'evS', phase: 'tq1', phaseLabel: 'Στροφή 1', phaseKind: 'turn', sheet: 'speed-turn', date: '04/11/2026 16:00', notes: '', scores }] }
});
const files = ['speed-file-1', 'speed-file-2', 'speed-file-3'];

// 1) The first report: every image inserted, tagged with its file; the boxes in one call; the times of the parts.
let r = call({ action: 'update', race: race({ 'x.one': { v: 1 } }, files.map((f, i) => snap('s' + i, f, i / 10))) });
assert.ok(r.ok && r.rebuilt, JSON.stringify(r));
let sh = sheetOf(r);
assert.strictEqual(sh._images.length, 3, 'three images');
assert.strictEqual(sh._inserted, 3, 'inserted once');
assert.deepStrictEqual(sh._images.map(im => im.alt).sort(), files.map(f => 'swim-image:' + f).sort(), 'tagged with their files');
assert.ok(sh._boxes > 0, 'boxes in one call');
const t1 = r.timings;
assert.ok(t1 && typeof t1.lock === 'number' && typeof t1.report === 'number' && typeof t1.total === 'number' && t1.rebuilt === true, JSON.stringify(t1));
assert.ok(t1.steps && typeof t1.steps.render === 'number' && t1.steps.imagesNew === 3 && t1.steps.imagesKept === 0, JSON.stringify(t1.steps));
assert.ok(G._console.some(l => /^swim update \{/.test(l)), 'the times in the log of the script');
const where = () => sh._images.map(im => [im.alt, im.row, im.col, im.ox, im.oy, im.w, im.h].join(':')).sort();
const before = where();

// 2) A score changes (the report is rebuilt): the images stay where they were, none downloaded again.
r = call({ action: 'update', race: race({ 'x.one': { v: 0 }, 'x.two': { v: 1 } }, files.map((f, i) => snap('s' + i, f, i / 10))) });
assert.ok(r.ok && r.rebuilt, 'a change of the evaluation rebuilds the report');
assert.strictEqual(sh._inserted, 3, 'nothing inserted again');
assert.deepStrictEqual(where(), before, 'the same images at the same places and sizes');
assert.ok(r.timings.steps.imagesKept === 3 && r.timings.steps.imagesNew === 0, JSON.stringify(r.timings.steps));

// 3) A new frame (a new error): only it is inserted; the others move to their new rows.
r = call({ action: 'update', race: race({ 'x.one': { v: 0 }, 'x.two': { v: 0 } }, files.concat(['speed-file-4']).map((f, i) => snap('s' + i, f, i / 10))) });
assert.strictEqual(sh._images.length, 4, 'four images');
assert.strictEqual(sh._inserted, 4, 'one more inserted');
assert.ok(r.timings.steps.imagesKept === 3 && r.timings.steps.imagesNew === 1, JSON.stringify(r.timings.steps));
const built = (() => { const rr = G.swimNormalizeRace_(race({}, files.concat(['speed-file-4']).map((f, i) => snap('s' + i, f, i / 10))));
  return G.swimBuildLayout_(rr, G.swimAnalyze_(rr), { pageHeight: w => G.swimPdfPageHeight_(reports, w) }); })();
built.images.forEach(want => {
  const im = sh._images.find(x => x.alt === 'swim-image:' + want.fileId);
  assert.ok(im && im.row === want.row, 'the image of ' + want.fileId + ' at row ' + want.row + ' (' + (im && im.row) + ')');
});

// 4) A frame taken away: its image goes, the others stay.
r = call({ action: 'update', race: race({ 'x.one': { v: 0 } }, ['speed-file-1', 'speed-file-3', 'speed-file-4'].map((f, i) => snap('s' + i, f, i / 10))) });
assert.strictEqual(sh._images.length, 3, 'three images again');
assert.ok(!sh._images.some(im => im.alt === 'swim-image:speed-file-2'), 'the image of the frame taken away went');
assert.strictEqual(sh._inserted, 4, 'nothing downloaded again');

// 5) Images of an older script (no tag) go once and come back tagged.
sh._images.forEach(im => { im.alt = ''; });
r = call({ action: 'update', race: race({ 'x.one': { v: 1 } }, ['speed-file-1', 'speed-file-3', 'speed-file-4'].map((f, i) => snap('s' + i, f, i / 10))) });
assert.strictEqual(sh._images.length, 3, 'three images');
assert.ok(sh._images.every(im => /^swim-image:/.test(im.alt)), 'tagged again');
assert.strictEqual(sh._inserted, 7, 'the untagged ones inserted again (once)');

// 6) The PDF: the times of the report, of the PDF and of the KOE App, and nothing rebuilt when nothing changed.
r = call({ action: 'pdf', race: race({ 'x.one': { v: 1 } }, ['speed-file-1', 'speed-file-3', 'speed-file-4'].map((f, i) => snap('s' + i, f, i / 10))) });
assert.ok(r.ok && r.pdf && r.pdf.url, JSON.stringify(r).slice(0, 300));
assert.ok(r.rebuilt === false, 'the same analysis: not rebuilt');
const tp = r.timings;
assert.ok(tp && ['lock', 'report', 'pdf', 'archive', 'total'].every(k => typeof tp[k] === 'number' && tp[k] >= 0), JSON.stringify(tp));
assert.ok(tp.total >= tp.report + tp.pdf + tp.archive - 1, 'the total covers the parts: ' + JSON.stringify(tp));
// (Only the Google charts of the report go in as images for the export, and out again after it.)
assert.strictEqual(sh._inserted - 7, sh._charts.length, 'the PDF of an unchanged analysis downloads no image again');
assert.strictEqual(sh._images.length, 3, 'the same three images after the PDF');
console.log('test_reportspeed: images kept and moved, boxes in one call, timings ok');
