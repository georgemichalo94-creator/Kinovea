// PDF σε σελίδες: μέτρηση της σελίδας από το (προσομοιωμένο) Google, τίποτα κομμένο ανάμεσα σε σελίδες,
// τελικό καρέ του Kinovea, σταθερά ύψη γραμμών και ευανάγνωστες γραμματοσειρές.
const { makeContext } = require('./mock_gas');
const assert = require('assert');

const G = makeContext({ athletes: [['unique_id', 'ΟΝΟΜΑΤΕΠΩΝΥΜΟ', 'ΦΥΛΟ'], [700001, 'ΔΟΚΙΜΑΣΤΙΚΗ ΑΘΛΗΤΡΙΑ', 'Γυναικών']] });
G.swimShowConnectionInfo = () => {};
G.swimSetup();
const call = body => JSON.parse(G.swimDoPost({ postData: { contents: JSON.stringify(Object.assign({ token: 'tok', app: 'kinovea-swim' }, body)) } }).text);
const reports = G._all[Object.keys(G._all).find(k => k !== 'DATA')];
const sheetOf = r => reports.getSheets().find(s => s.getSheetId() === r.sheetId);

// ---------------------------------------------------------------------------------------------
// 1) Μέτρηση κειμένου: όχι λιγότερο από όσο χρειάστηκε το Google Sheets σε πραγματικό PDF
//    (κεφαλίδες 8 pt σε στήλες 84 px: η γραμμή της εκκίνησης μεγάλωσε στα 54 px, του Timing Data έμεινε 44).
// ---------------------------------------------------------------------------------------------
const px8 = 8 * 4 / 3;
const tall = ['Χρόνος Αντίδρασης – πρώτη κίνηση (sec)', 'Μέση ταχύτητα από το σήμα έως την έξοδο (m/s)', 'Απόσταση Εξόδου από το νερό (κεφάλι) (μ.)']
  .map(t => G.swimTextLines_(t, 84 - 8, px8, true));
assert.ok(Math.max(...tall) * px8 * G.SWIM_LINE + 6 >= 54, 'start headers need ≥ 54 px: ' + tall);
assert.ok(G.swimTextLines_('Distance (m)', 76, px8, true) === 1);
assert.ok(G.swimTextLines_('Μέση ταχύτητα από εκκίνηση (m/s)', 76, px8, true) <= 4);
assert.strictEqual(G.swimTextLines_('α\nβ\nγ', 500, 16, false), 3, 'line breaks');
assert.strictEqual(G.swimFontSize_(10), 12, 'body text 10 → 12 pt');
assert.strictEqual(G.swimFontSize_(8), 10, 'headers 8 → 10 pt');
assert.strictEqual(G.swimLayoutWidth_(15), 1124, 'columns A-O: 1124 px');
// Στο A4 τα νούμερα (12 pt) βγαίνουν ~8 pt (πριν 10 pt σε 1387 px → 5.2 pt).
const printPt = 12 * (8.27 - 0.5) * 96 / (1124 + 15);
assert.ok(printPt > 7.8, 'body text in the PDF: ' + printPt.toFixed(1) + ' pt');

// ---------------------------------------------------------------------------------------------
// 2) Μέτρηση της σελίδας: ακρίβεια 1 px, μία φορά, με αποτυχία → υπολογισμός από το A4
// ---------------------------------------------------------------------------------------------
const W = 1124;
const truth = Math.floor((W + 15) * (11.69 - 0.8 - G._pdf.footer) / (8.27 - 0.5));
let n0 = G._exports.length;
const H = G.swimPdfPageHeight_(reports, W);
assert.strictEqual(H, truth, 'measured page height ' + H + ' vs ' + truth);
const calls = G._exports.length - n0;
assert.ok(calls >= 6 && calls <= 14, 'exports for the measurement: ' + calls);
assert.ok(!reports.getSheets().some(s => s.getName().indexOf('_swim_pdf_pages') === 0), 'temporary sheet removed');
n0 = G._exports.length;
assert.strictEqual(G.swimPdfPageHeight_(reports, W), H);
assert.strictEqual(G._exports.length, n0, 'kept in the script properties');
assert.ok(Math.abs(G.swimPdfEstimatedHeight_(W) - H) / H < 0.03, 'estimate close to the measurement: ' + G.swimPdfEstimatedHeight_(W));
// Αποτυχία (π.χ. PDF που δεν διαβάζεται): ο υπολογισμός από το A4, χωρίς νέα δοκιμή για μία ώρα.
const fetch = G.UrlFetchApp.fetch;
G.UrlFetchApp.fetch = () => ({ getResponseCode: () => 200, getBlob: () => ({ getDataAsString: () => 'not a pdf', setName() { return this; } }) });
assert.strictEqual(G.swimPdfPageHeight_(reports, 1272), G.swimPdfEstimatedHeight_(1272), 'fallback');
assert.ok(JSON.parse(G._props['SWIM_PDF_PAGE_1272']).failed, 'failure remembered');
G.UrlFetchApp.fetch = () => { throw new Error('should not retry yet'); };
assert.strictEqual(G.swimPdfPageHeight_(reports, 1272), G.swimPdfEstimatedHeight_(1272));
G.UrlFetchApp.fetch = fetch;
delete G._props['SWIM_PDF_PAGE_1272'];

// ---------------------------------------------------------------------------------------------
// 3) Μεγάλο report: τίποτα δεν κόβεται ανάμεσα σε σελίδες (πάνω στα ύψη που μπήκαν στο φύλλο)
// ---------------------------------------------------------------------------------------------
const jpeg = Buffer.from([0xff, 0xd8, 0xff, 0xe0, 1, 2, 3, 4, 0xff, 0xd9]).toString('base64');
const img = id => call({ action: 'image', raceId: 'p400', snapshotId: id, mime: 'image/jpeg', data: jpeg }).fileId;
const cps = []; for (let w = 0; w < 400; w += 50) [15, 25, 35, 45, 50].forEach(p => cps.push(w + p));
const times = cps.map(d => +(d * 0.66 + (d % 50 === 15 ? 0.4 : 0)).toFixed(2));
const strokes = []; for (let t = 4.9; t < times[times.length - 1] - 0.3; t += 0.7) strokes.push(+t.toFixed(2));
const final = img('final');
const snaps = [];
for (let i = 1; i <= 4; i++) snaps.push({ id: 's' + i, t: i * 30.5, phase: i === 1 ? 'start' : '', stage: 'Στάδιο ' + i,
  comment: 'Σχόλιο '.repeat(20 * i), fileId: img('s' + i), eliteFileId: i % 2 ? img('e' + i) : '', eliteLabel: 'Elite ' + i });
const race400 = { id: 'p400', athlete: 'ΔΟΚΙΜΑΣΤΙΚΗ ΑΘΛΗΤΡΙΑ', athleteId: '700001', gender: 'Γυναικών', style: 'fs', distance: 400, pool: 50,
  competition: 'Δοκιμή σελίδων', date: '11.10.2026', round: 'Final',
  checkpoints: cps.map((d, i) => ({ d, t: times[i] })), strokes,
  finalFrame: { fileId: final, t: times[times.length - 1] },
  tech: { phases: [{ id: 'start', kind: 'start', values: { reaction: 0.6, t15: 6.3, kicks: 6, breakoutDist: 12 } }],
    angles: [], snapshots: snaps, values: { notes: 'Σημείωση '.repeat(60) } } };
let r = call({ action: 'pdf', race: race400 });
assert.ok(r.ok && r.rebuilt && r.pdf && r.pdf.url, JSON.stringify(r).slice(0, 300));
const sh = sheetOf(r);
const race = G.swimNormalizeRace_(race400);
const layout = G.swimBuildLayout_(race, G.swimAnalyze_(race), Object.assign(G.swimComparisons_(race), { pageHeight: w => G.swimPdfPageHeight_(reports, w) }));
assert.strictEqual(layout.page.height, H, 'layout uses the measured page');
assert.ok(layout.page.fillers > 0, 'page breaks were needed');
assert.deepStrictEqual(JSON.parse(JSON.stringify(r.print)), JSON.parse(JSON.stringify(layout.print)), 'print area in the response');

// Τα ύψη στο φύλλο: όλες οι γραμμές σταθερές και ίδιες με το layout.
for (let row = 1; row <= layout.print.rows; row++) {
  assert.ok(sh._forced[row], 'row ' + row + ' has a fixed height');
  assert.strictEqual(sh._rowHeights[row], G.swimRowHeight_(layout.grid, row), 'row ' + row + ' height');
}
// Σελίδες όπως του Google: η σελίδα κάθε γραμμής.
const pageOf = {};
let page = 1, y = 0;
for (let row = 1; row <= layout.print.rows + 40; row++) {
  const h = sh._rowHeights[row] || 21;
  if (y > 0 && y + h > H) { page++; y = h; } else y += h;
  pageOf[row] = page;
}
const rowsFor = (row, height) => { let yy = 0, rr = row; while (yy < height) { yy += sh._rowHeights[rr] || 21; rr++; } return rr - 1; };
sh._charts.forEach((c, i) => {
  const spec = layout.charts[i];
  const last = rowsFor(c.pos[0], spec.height + (c.pos[3] || 0));
  assert.strictEqual(pageOf[c.pos[0]], pageOf[last], `chart "${spec.title}" rows ${c.pos[0]}-${last} on pages ${pageOf[c.pos[0]]}/${pageOf[last]}`);
});
assert.strictEqual(sh._images.length, layout.images.length, 'all images inserted');
sh._images.forEach(im => {
  const last = rowsFor(im.row, im.oy + im.h);
  assert.strictEqual(pageOf[im.row], pageOf[last], `image at row ${im.row} cut between pages`);
});
// Γραφήματα, εικόνες, στιγμιότυπα: ολόκληρα σε μία σελίδα· πίνακες ως 40% της σελίδας ολόκληροι, οι μεγαλύτεροι
// τουλάχιστον με τον τίτλο και τις πρώτες γραμμές τους.
let blocks = 0, split = 0;
Object.keys(layout.paging.blocks).forEach(id => {
  const b = layout.paging.blocks[id];
  const whole = /^(charts|finalFrame|snapshot)#/.test(id);
  let h = 0; for (let row = b.first; row <= b.last; row++) h += sh._rowHeights[row];
  if (h > H) return;
  blocks++;
  if (whole || h <= H * G.SWIM_KEEP_TABLE) {
    assert.strictEqual(pageOf[b.first], pageOf[b.last], `section ${id} (rows ${b.first}-${b.last}) cut between pages`);
  } else {
    split += pageOf[b.first] !== pageOf[b.last] ? 1 : 0;
    assert.strictEqual(pageOf[b.first], pageOf[b.headLast], `title of ${id} without its first rows`);
  }
});
assert.ok(blocks >= 15, 'sections checked: ' + blocks);
// Το PDF: περιοχή εκτύπωσης A1:O<τελευταία>, με τις σελίδες που υπολογίστηκαν.
const exp = G._exports[G._exports.length - 1];
assert.strictEqual(exp.name, sh.getName());
assert.strictEqual(exp.r1, 0); assert.strictEqual(exp.c1, 0);
assert.strictEqual(exp.r2, layout.print.rows); assert.strictEqual(exp.c2, 15);
assert.strictEqual(exp.pages, pageOf[layout.print.rows], 'pages of the PDF');
assert.ok(G._sleeps.indexOf(4000) >= 0, 'wait for the new charts before the PDF');
// Γραφήματα και εικόνες μέσα στις στήλες A-O.
const colLeft = c => { let x = 0; for (let k = 1; k < c; k++) x += G.swimColumnWidth_(k); return x; };
sh._charts.forEach((c, i) => assert.ok(colLeft(c.pos[1]) + c.pos[2] + layout.charts[i].width <= 1124, 'chart inside the page: ' + layout.charts[i].title));
sh._images.forEach(im => assert.ok(colLeft(im.col) + im.ox + im.w <= 1124, 'image inside the page'));

// ---------------------------------------------------------------------------------------------
// 4) Τελικό καρέ: μεγάλο, στο κέντρο, με τις αναλογίες του· αλλαγή εικόνας → ανακατασκευή
// ---------------------------------------------------------------------------------------------
const ff = layout.images.find(im => im.fileId === final);
assert.ok(ff && ff.fit && ff.width === 900 && ff.height === 506, JSON.stringify(ff));
const ffSheet = sh._images.find(im => im.blob === G._drive._files[final]._blob);
assert.ok(ffSheet && ffSheet.w === 900 && ffSheet.h === 506, 'final frame 16:9: ' + JSON.stringify([ffSheet.w, ffSheet.h]));
assert.strictEqual(colLeft(ffSheet.col) + ffSheet.ox, 112, 'centered');
const title = Object.values(layout.grid.cells).find(c => typeof c.v === 'string' && /^Τερματισμός στο βίντεο/.test(c.v));
assert.ok(title && /καρέ στο \d+:\d\d\.\d\d s/.test(title.v), title && title.v);
assert.strictEqual(race.finalFrame.fileId, final);
assert.strictEqual(G.swimNormalizeRace_(Object.assign({}, race400, { finalFrame: { fileId: '' } })).finalFrame, null);
assert.strictEqual(G.swimNormalizeRace_(Object.assign({}, race400, { finalFrame: 'x' })).finalFrame, null);
// 4:3 εικόνα: ύψος 506, πλάτος 675, στο κέντρο
G._drive._files[final]._blob._size = [960, 720];
const other = img('final');
G._drive._files[other]._blob._size = [960, 720];
r = call({ action: 'update', race: Object.assign({}, race400, { finalFrame: { fileId: other, t: 245.1 } }) });
assert.ok(r.ok && r.rebuilt, 'new final frame rebuilds the sheet');
const im43 = sheetOf(r)._images.find(im => im.blob === G._drive._files[other]._blob);
assert.ok(im43 && im43.h === 506 && im43.w === 675, JSON.stringify(im43 && [im43.w, im43.h]));
assert.strictEqual(colLeft(im43.col) + im43.ox, 112 + Math.round((900 - 675) / 2), 'centered 4:3');
r = call({ action: 'update', race: Object.assign({}, race400, { finalFrame: { fileId: other, t: 245.1 } }) });
assert.ok(r.ok && !r.rebuilt, 'same data: no rebuild');
// Χωρίς τελικό καρέ: ούτε ενότητα ούτε εικόνα
const plain = G.swimNormalizeRace_(Object.assign({}, race400, { finalFrame: null }));
const lp = G.swimBuildLayout_(plain, G.swimAnalyze_(plain), {});
assert.ok(!Object.values(lp.grid.cells).some(c => typeof c.v === 'string' && /^Τερματισμός στο βίντεο/.test(c.v)));

// ---------------------------------------------------------------------------------------------
// 5) Χωρίς αλλαγές σελίδας (πολύ ψηλή σελίδα) το layout είναι το ίδιο με το πρώτο πέρασμα
// ---------------------------------------------------------------------------------------------
const huge = G.swimBuildLayout_(race, G.swimAnalyze_(race), { pageHeight: () => 1e9 });
assert.strictEqual(huge.page.fillers, 0);
const first = G.swimBuildRaceLayout_(race, G.swimAnalyze_(race), {}, G.swimNewPaging_(0, null));
assert.strictEqual(huge.grid.maxRow, first.grid.maxRow);
assert.strictEqual(JSON.stringify(huge.charts.map(c => c.row)), JSON.stringify(first.charts.map(c => c.row)));
assert.notStrictEqual(huge.signature, layout.signature, 'the page height is part of the signature');

// ---------------------------------------------------------------------------------------------
// 6) PDF από το μενού: πρώτα το φύλλο (με τις σελίδες), μετά η εξαγωγή της περιοχής του
// ---------------------------------------------------------------------------------------------
const sum = G._ss.getSheetByName('Αναλύσεις Αγώνων');
const ids = sum.getRange(2, 1, sum.getLastRow() - 1, 30).getValues().map(row => row.indexOf('p400'));
const rowIdx = ids.findIndex(i => i >= 0);
assert.ok(rowIdx >= 0, 'race in the summary');
sum._select([[rowIdx + 2, 1]]);
const before = G._exports.length;
G.swimPdfSelected();
const last = G._exports[G._exports.length - 1];
assert.ok(G._exports.length === before + 1 && last.r2 > 0 && last.c2 === 15, JSON.stringify(last));
assert.ok(G._ui._dialogs.some(d => d.title === 'Report PDF'));
// Νέα μέτρηση από το μενού
G.swimPdfPagesCheck();
assert.ok(G._ui._dialogs.some(d => d.title === 'Σελίδες PDF' && /Μετρήθηκε/.test(d.html)), 'measurement dialog');

console.log('pages OK: page ' + H + ' px, fillers ' + layout.page.fillers + ', blocks ' + blocks + ' (' + split + ' long tables continue), PDF pages ' + exp.pages);
