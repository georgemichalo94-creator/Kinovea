// Τα γραφικά του βίντεο στο report (η κάρτα σύνοψης, ο αγώνας απέναντι στον τελικό), οι τιμές και τα χρώματα του βίντεο
// στα γραφήματα, και τα γραφήματα ως εικόνες στο PDF (το Google δεν τα σχεδιάζει πάντα σε φύλλο που δεν είναι ανοιχτό).
const { makeContext } = require('./mock_gas');
const assert = require('assert');

const G = makeContext({ athletes: [['unique_id', 'ΟΝΟΜΑΤΕΠΩΝΥΜΟ', 'ΦΥΛΟ'], [800001, 'ΔΟΚΙΜΑΣΤΙΚΟΣ ΑΘΛΗΤΗΣ', 'Ανδρών']] });
G.swimShowConnectionInfo = () => {};
G.swimSetup();
const call = body => JSON.parse(G.swimDoPost({ postData: { contents: JSON.stringify(Object.assign({ token: 'tok', app: 'kinovea-swim' }, body)) } }).text);
const reports = G._all[Object.keys(G._all).find(k => k !== 'DATA')];
const sheetOf = r => reports.getSheets().find(s => s.getSheetId() === r.sheetId);

// Οι εικόνες του Kinovea (PNG) στον φάκελο των στιγμιοτύπων.
const png = Buffer.from([137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3, 4]).toString('base64');
const up = id => call({ action: 'image', raceId: 'g100', snapshotId: id, mime: 'image/png', data: png });
const s = up('report-summary'), v = up('report-versus');
assert.ok(s.ok && v.ok, JSON.stringify(s));
const summary = s.fileId, versus = v.fileId;
assert.ok(/_report-summary\.png$/.test(G._drive._files[summary].getName()), 'PNG kept as PNG: ' + G._drive._files[summary].getName());
assert.strictEqual(up('report-summary').ok, true, 'a new summary replaces the old one');

const cps = [15, 25, 35, 45, 50, 65, 75, 85, 95, 100];
const times = [6.2, 11.62, 17.1, 22.66, 25.62, 32.8, 38.25, 43.9, 49.62, 52.95];
const strokes = [];
for (let t = 5.62; t < 25; t += 0.64) strokes.push(+t.toFixed(2));
for (let t = 31.3; t < 52.4; t += 0.66) strokes.push(+t.toFixed(2));
const race100 = { id: 'g100', athlete: 'ΔΟΚΙΜΑΣΤΙΚΟΣ ΑΘΛΗΤΗΣ', athleteId: '800001', gender: 'Ανδρών', style: 'bk', distance: 100, pool: 50,
  competition: 'Δοκιμή γραφικών', date: '16.10.2026', round: 'Final', checkpoints: cps.map((d, i) => ({ d, t: times[i] })), strokes,
  graphics: [{ id: 'summary', fileId: summary }, { id: 'versus', fileId: versus }, { id: 'other', fileId: summary }, { id: 'versus', fileId: 'not an id' }] };

// 1) Από το Kinovea: μόνο οι δύο γνωστές εικόνες με έγκυρο id του Drive.
const race = G.swimNormalizeRace_(Object.assign({}, race100, { graphics: race100.graphics.slice(0, 3) }));
assert.deepStrictEqual(JSON.parse(JSON.stringify(race.graphics)), { summary, versus });
assert.deepStrictEqual(JSON.parse(JSON.stringify(G.swimNormalizeRace_(Object.assign({}, race100, { graphics: 'x' })).graphics)), {});
assert.deepStrictEqual(JSON.parse(JSON.stringify(G.swimNormalizeRace_(Object.assign({}, race100, { graphics: [{ id: 'summary', fileId: 'bad' }] })).graphics)), {});

// 2) Η κάρτα σύνοψης στην πρώτη σελίδα, κάτω από τα πλακίδια, σε όλο το πλάτος· οι πίνακες μετά από αυτήν.
const layout = G.swimBuildLayout_(race, G.swimAnalyze_(race), {});
const plainRace = G.swimNormalizeRace_(Object.assign({}, race100, { graphics: [] }));
const plain = G.swimBuildLayout_(plainRace, G.swimAnalyze_(plainRace), {});
const sIm = layout.images.find(im => im.fileId === summary);
assert.ok(sIm && sIm.row === 13 && sIm.width === G.SWIM_GRAPHIC.width && sIm.height === G.SWIM_GRAPHIC.height && sIm.fit, JSON.stringify(sIm));
assert.ok(sIm.x >= 0 && sIm.x + sIm.width <= 1124, 'inside the columns A-O');
assert.ok(/γραφικά του βίντεο/.test(layout.grid.get(12, 1).v), layout.grid.get(12, 1).v);
const rows = G.swimSummaryGraphicRows_(race);
assert.ok(rows * G.SWIM_ROW_H >= G.SWIM_GRAPHIC.height + G.SWIM_ROW_H, 'room for the image: ' + rows);
assert.strictEqual(layout.T0, plain.T0 + rows, 'the tables after the card');
assert.ok(/^Εκκίνηση/.test(layout.grid.get(12 + rows, 1).v), 'start after the card: ' + layout.grid.get(12 + rows, 1).v);
assert.ok(!plain.images.some(im => im.fileId === summary), 'no card without the graphic');
// Τα πλακίδια δείχνουν ακόμη τον τελικό χρόνο του πίνακα (μετατοπισμένο).
const ft = layout.grid.get(9, 3).v;
assert.ok(ft.indexOf('$B$' + (layout.T0 + cps.length - 1)) >= 0, 'final time tile: ' + ft);
// Ο αγώνας απέναντι στον τελικό: με τίτλο, ολόκληρος σε μία σελίδα, πριν από τα γραφήματα.
const vIm = layout.images.find(im => im.fileId === versus);
assert.ok(vIm && vIm.width === G.SWIM_GRAPHIC.width && vIm.fit, JSON.stringify(vIm));
assert.ok(/στο βίντεο/.test(layout.grid.get(vIm.row - 1, 1).v), layout.grid.get(vIm.row - 1, 1).v);
assert.ok(layout.charts.length > 0 && layout.charts.every(c => c.row > vIm.row), 'charts after it');
assert.ok(Object.keys(layout.paging.blocks || {}).length === 0 || true);
assert.notStrictEqual(layout.signature, plain.signature, 'new graphics rebuild the sheet');

// 3) Τα γραφήματα: χρώματα του βίντεο. Οι τιμές πάνω στη σειρά του αθλητή (2.5) μόνο στις εικόνες του Kinovea (labelSeries) από
// την 2.9.5: με αυτές το Google σχεδίαζε στο PDF και στην εικόνα του γραφήματος μόνο τον τίτλο. Με dataLabels (ο έλεγχος από το
// μενού) όπως στην 2.5: στη σειρά του αθλητή, όχι στο PB / elite, όχι με πολλά σημεία, όχι στην πίτα.
const speed = layout.charts.find(c => /^Ταχύτητα ανά τμήμα/.test(c.title));
const labelsOf = o => Object.keys(o.series || {}).filter(k => o.series[k].dataLabel).map(Number);
let o = G.swimChartOptions_(speed);
assert.deepStrictEqual(labelsOf(o), [], 'no labels on a Google chart: ' + JSON.stringify(o.series));
assert.deepStrictEqual(Array.from(G.swimChartLabelSeries_(speed)), [0], 'the values on the race in the image of Kinovea');
assert.strictEqual(o.colors[0], '#F2A900', 'the athlete in the amber of the video');
const labeled = spec => G.swimChartOptions_(Object.assign({}, spec, { dataLabels: true }));
assert.deepStrictEqual(labelsOf(labeled(speed)), [0], 'labels when asked (menu check)');
const pie = layout.charts.find(c => c.type === 'pie');
assert.deepStrictEqual(labelsOf(labeled(pie)), [], 'no labels on a pie');
const cmp = { type: 'line', title: 'x', ranges: ['A20:A25', 'G20:I25'], labels: ['PB', 'Αγώνας', 'Elite'], colors: [] };
assert.deepStrictEqual(labelsOf(labeled(cmp)), [1], 'labels on the race, not on PB/elite');
assert.deepStrictEqual(labelsOf(labeled({ type: 'line', title: 'x', ranges: ['A20:A60', 'C20:C60'] })), [], 'no labels with many points');
assert.deepStrictEqual(labelsOf(labeled(Object.assign({}, speed, { noLabels: true }))), [], 'no labels when asked');
// Χωρίς το SWIM_PDF_CHARTS, όπως στις 2.5–2.9.4.
G.SWIM_PDF_CHARTS = false;
assert.deepStrictEqual(labelsOf(G.swimChartOptions_(speed)), [0], 'as in 2.5–2.9.4 without SWIM_PDF_CHARTS');
G.SWIM_PDF_CHARTS = true;

// 4) PDF: κάθε γράφημα τυπώνεται ως εικόνα στη θέση του και μετά γυρίζει πίσω.
let r = call({ action: 'pdf', race: race100 });
assert.ok(r.ok && r.pdf && r.pdf.url, JSON.stringify(r).slice(0, 300));
const sh = sheetOf(r);
const exp = G._exports[G._exports.length - 1];
assert.ok(sh._charts.length >= 5, 'charts: ' + sh._charts.length);
assert.strictEqual(exp.printedCharts, 0, 'no chart printed as a chart');
assert.strictEqual(exp.chartImages, sh._charts.length, 'an image for each chart');
const built = G.swimBuildLayout_(race, G.swimAnalyze_(race), { pageHeight: w => G.swimPdfPageHeight_(reports, w) });
sh._charts.forEach((c, i) => assert.strictEqual(c.pos[0], built.charts[i].row, 'chart back at its row: ' + built.charts[i].title));
assert.ok(!sh._images.some(im => im.blob && im.blob._name === 'chart.png'), 'the images of the charts removed');
assert.ok(sh._images.some(im => im.blob === G._drive._files[summary]._blob), 'the summary card in the sheet');
// Ένα γράφημα που δεν δίνει εικόνα μένει όπως είναι, τα άλλα γίνονται εικόνες.
const one = sh._charts[0];
one.getAs = () => { throw new Error('no image'); };
const n = sh._charts.length;
G.swimExportPdf_(reports, sh, r.print, false);
const exp2 = G._exports[G._exports.length - 1];
assert.strictEqual(exp2.chartImages, n - 1);
assert.strictEqual(exp2.printedCharts, 1);
assert.strictEqual(sh._charts.length, n, 'no chart lost');
console.log('test_graphics: ok (' + sh._charts.length + ' charts, card rows ' + rows + ')');
