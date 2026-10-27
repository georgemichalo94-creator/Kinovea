// Τα γραφήματα του report σχεδιασμένα από το Kinovea (2.6): οι τιμές τους από το φύλλο, οι εικόνες στη θέση τους όσο είναι
// για τις τρέχουσες τιμές, το PDF που περιμένει τα γραφήματα, ο πίνακας των φιναλίστ σε όλο το πλάτος, η έκδοση στο φύλλο.
const { makeContext } = require('./mock_gas');
const assert = require('assert');

const G = makeContext({ athletes: [['unique_id', 'ΟΝΟΜΑΤΕΠΩΝΥΜΟ', 'ΦΥΛΟ'], [800002, 'ΔΟΚΙΜΑΣΤΙΚΗ ΑΘΛΗΤΡΙΑ', 'Γυναικών']] });
G.swimShowConnectionInfo = () => {};
G.swimSetup();
const call = body => JSON.parse(G.swimDoPost({ postData: { contents: JSON.stringify(Object.assign({ token: 'tok', app: 'kinovea-swim' }, body)) } }).text);
const reports = G._all[Object.keys(G._all).find(k => k !== 'DATA')];
const sheetOf = r => reports.getSheets().find(s => s.getSheetId() === r.sheetId);
const png = Buffer.from([137, 80, 78, 71, 13, 10, 26, 10, 9, 9, 9]).toString('base64');

const cps = [15, 25, 35, 45, 50, 65, 75, 85, 95, 100];
const times = [6.72, 12.48, 18.36, 24.26, 27.42, 36.1, 42.12, 48.36, 54.76, 57.94];
const strokes = [];
for (let t = 5.9; t < 27; t += 1.12) strokes.push(+t.toFixed(2));
for (let t = 33.2; t < 57.6; t += 1.18) strokes.push(+t.toFixed(2));
const finals = {
  label: 'Δοκιμαστικός τελικός · 100μ. fly Γυναικών', source: 'δοκιμή',
  swimmers: [1, 2, 3].map(rank => ({ rank, name: 'Φιναλίστ ' + rank, times: [{ d: 50, t: 25.4 + rank * 0.2 }, { d: 100, t: 55.4 + rank * 0.5 }] })),
  metrics: [
    { group: 'Εκκίνηση', label: 'Αποκόλληση', athlete: '0.68', rank: '4/4', winner: '0.63', mean: '0.70', best: '0.59', diff: '+0.05' },
    { group: 'Τερματισμός', label: 'Τελευταία 5μ.', athlete: '3.18', rank: '4/4', winner: '3.02', mean: '3.06', best: '2.86', diff: '+0.16' },
    { group: 'Συχνότητα', label: '50–100μ.', athlete: '52.5 κύκλοι/λ.', rank: '2/4', winner: '54.1 κύκλοι/λ.', mean: '53.0 κύκλοι/λ.', best: '55.0 κύκλοι/λ.', diff: '−1.6' }
  ]
};
const race = () => ({ id: 'c100', athlete: 'ΔΟΚΙΜΑΣΤΙΚΗ ΑΘΛΗΤΡΙΑ', athleteId: '800002', gender: 'Γυναικών', style: 'fly', distance: 100, pool: 50,
  competition: 'Δοκιμή γραφημάτων', date: '16.10.2026', round: 'Final', checkpoints: cps.map((d, i) => ({ d, t: times[i] })), strokes, finals });

// 1) Κλειδιά: σταθερά, μοναδικά, ίδια σε κάθε κατασκευή του layout.
const norm = G.swimNormalizeRace_(race());
const extrasOf = n => Object.assign(G.swimComparisons_(n), { pageHeight: w => G.swimPdfPageHeight_(reports, w) });
const L1 = G.swimBuildLayout_(norm, G.swimAnalyze_(norm), extrasOf(norm));
const L2 = G.swimBuildLayout_(norm, G.swimAnalyze_(norm), extrasOf(norm));
const keys = L1.charts.map(c => c.key);
assert.ok(keys.length >= 8, 'charts: ' + keys.length);
assert.strictEqual(new Set(keys).size, keys.length, 'unique keys');
assert.ok(keys.every(k => /^chart-[0-9a-f]{8}$/.test(k)), keys.join(','));
assert.deepStrictEqual(L2.charts.map(c => c.key), keys, 'stable keys');
assert.ok(L1.charts.every(c => c.type === 'pie' || c.type === 'radar' || (c.labels && c.labels.length)), 'every chart names its series');

// 2) Η πρώτη ενημέρωση: γραφήματα του Google στη θέση τους, οι τιμές όλων επιστρέφονται όταν τις ζητά το Kinovea.
let r = call({ action: 'update', race: race(), drawCharts: true });
assert.ok(r.ok, JSON.stringify(r).slice(0, 300));
const sh = sheetOf(r);
assert.strictEqual(r.chartsMissing.length, keys.length);
assert.strictEqual(r.charts.length, keys.length);
assert.strictEqual(sh._charts.length, keys.length, 'Google charts placed');
assert.strictEqual(sh._images.filter(im => /^swim-chart:/.test(im.alt || '')).length, 0);
r.charts.forEach(c => {
  assert.ok(/^[0-9a-f]{8}$/.test(c.hash), 'hash ' + c.hash);
  assert.ok(c.title && c.width === 546 && c.height === 300, JSON.stringify(c).slice(0, 200));
  assert.ok(c.series.length >= 1, 'series of ' + c.title);
  c.series.forEach(s => {
    assert.strictEqual(s.values.length, c.categories.length, 'values of ' + c.title);
    assert.strictEqual(s.texts.length, c.categories.length, 'texts of ' + c.title);
    assert.ok(/^#[0-9A-Fa-f]{6}$/.test(s.color), 'color ' + s.color);
    assert.ok(s.type === 'line' || s.type === 'bars');
    assert.ok(s.axis === 0 || s.axis === 1);
  });
});
const speed = r.charts.find(c => /^Ταχύτητα ανά τμήμα/.test(c.title));
assert.deepStrictEqual(speed.categories, cps.map(String), 'distances as categories');
assert.strictEqual(speed.series[0].color, '#F2A900', 'the athlete in amber');
assert.deepStrictEqual(Array.from(speed.labelSeries), [0], 'values on the race');
const combo = r.charts.find(c => /^Συχνότητα – μήκος/.test(c.title));
assert.deepStrictEqual(combo.series.map(s => s.type), ['bars', 'line', 'line']);
assert.deepStrictEqual(combo.series.map(s => s.axis), [0, 1, 1]);
assert.deepStrictEqual(combo.series.map(s => s.name), ['Συχνότητα', 'Μήκος χεριάς', 'Ταχύτητα']);
// Χωρίς drawCharts δεν στέλνονται οι τιμές.
assert.strictEqual(call({ action: 'update', race: race() }).charts, undefined);

// 3) PDF από το Kinovea: πρώτα τα γραφήματα (χωρίς PDF).
const exports0 = G._exports.length;
r = call({ action: 'pdf', race: race(), drawCharts: true });
assert.ok(r.ok && r.needCharts === true && !r.pdf, JSON.stringify(r).slice(0, 200));
assert.strictEqual(G._exports.length, exports0, 'no PDF yet');
assert.strictEqual(r.charts.length, keys.length);

// 4) Οι εικόνες ανεβαίνουν μαζί (και μία με λάθος id δεν σταματά τις άλλες).
const up = call({ action: 'images', raceId: 'c100', images: r.charts.map(c => ({ id: c.key, mime: 'image/png', data: png })).concat([{ id: 'bad id!', mime: 'image/png', data: png }]) });
assert.ok(up.ok, JSON.stringify(up).slice(0, 200));
assert.strictEqual(up.files.length, keys.length + 1);
assert.ok(up.files[keys.length].error && !up.files[keys.length].fileId, 'bad id rejected');
const hashOf = {};
r.charts.forEach(c => { hashOf[c.key] = c.hash; });
const chartImages = up.files.filter(f => f.fileId).map(f => ({ id: f.id, fileId: f.fileId, hash: hashOf[f.id] }));
assert.ok(/_chart-[0-9a-f]{8}\.png$/.test(G._drive._files[chartImages[0].fileId].getName()), G._drive._files[chartImages[0].fileId].getName());
assert.strictEqual(call({ action: 'images', raceId: 'c100', images: [] }).ok, false, 'nothing to upload');

// 5) Το PDF με τις εικόνες: καμία ανάγκη για γραφήματα του Google.
const withImages = Object.assign(race(), { chartImages });
r = call({ action: 'pdf', race: withImages, drawCharts: true });
assert.ok(r.ok && !r.needCharts && r.pdf && r.pdf.url, JSON.stringify(r).slice(0, 300));
assert.strictEqual(r.chartsMissing.length, 0);
assert.strictEqual(r.charts, undefined, 'no chart values when nothing is missing');
let exp = G._exports[G._exports.length - 1];
assert.strictEqual(exp.kinoveaCharts, keys.length, 'the images of Kinovea printed');
assert.strictEqual(exp.printedCharts, 0);
assert.strictEqual(exp.chartImages, 0, 'no Google chart images needed');
assert.strictEqual(sh._charts.length, 0, 'no Google chart left');
const normImages = G.swimNormalizeRace_(withImages);
const layout = G.swimBuildLayout_(normImages, G.swimAnalyze_(normImages), extrasOf(normImages));
layout.charts.forEach(spec => {
  const im = sh._images.find(x => x.alt === 'swim-chart:' + spec.key);
  assert.ok(im, 'image of ' + spec.title);
  assert.deepStrictEqual([im.row, im.col, im.ox, im.w, im.h], [spec.row, spec.col, spec.offsetX || 0, spec.width, spec.height], spec.title);
});
// Ξανά το ίδιο: τίποτα δεν ξαναμπαίνει.
const inserted = sh._inserted;
r = call({ action: 'update', race: withImages });
assert.strictEqual(sh._inserted, inserted, 'no new image');
assert.strictEqual(r.chartsMissing.length, 0);

// 6) Μια τιμή άλλαξε στο φύλλο (π.χ. διορθώθηκε με το χέρι): μόνο εκείνο το γράφημα γίνεται ξανά γράφημα του Google.
const speedSpec = layout.charts.find(c => /^Ταχύτητα ανά τμήμα/.test(c.title));
const cell = speedSpec.ranges[1].split(':')[0];
sh.getRange(cell).setValue(2.41);
// Τα γραφήματα με αυτό το κελί: η ταχύτητα και ο συνδυασμός συχνότητας – μήκους – ταχύτητας.
const box = G.swimA1Box_(cell);
const touched = layout.charts.filter(c => c.ranges.some(a => { const b = G.swimA1Box_(a); return b.r1 <= box.r1 && box.r1 <= b.r2 && b.c1 <= box.c1 && box.c1 <= b.c2; })).map(c => c.key);
assert.strictEqual(touched.length, 2, touched.join(','));
r = call({ action: 'update', race: withImages, drawCharts: true });
assert.deepStrictEqual(Array.from(r.chartsMissing), Array.from(touched));
assert.strictEqual(r.charts.length, 2, 'only the values of the missing charts');
assert.strictEqual(r.charts.find(c => c.key === speedSpec.key).series[0].values[0], 2.41);
assert.strictEqual(sh._charts.length, 2, 'two Google charts');
assert.strictEqual(sh._images.filter(im => /^swim-chart:/.test(im.alt || '')).length, keys.length - 2);
// Το PDF χωρίς drawCharts (π.χ. μετά το ανέβασμα): γίνεται με όσα υπάρχουν, τα γραφήματα του Google ως εικόνες του Google.
r = call({ action: 'pdf', race: withImages });
exp = G._exports[G._exports.length - 1];
assert.ok(r.pdf && exp.kinoveaCharts === keys.length - 2 && exp.chartImages === 2 && exp.printedCharts === 0, JSON.stringify(exp));
assert.strictEqual(sh._charts.length, 2, 'the Google charts back after the PDF');

// 7) Μια εικόνα που δεν υπάρχει πια στο Drive: γράφημα του Google και ξανά στα missing.
G._drive._files[chartImages[1].fileId].getBlob = () => { throw new Error('trashed'); };
const index = G.swimIndexSheet_(reports);
index.getRange(G.swimIndexRow_(index, 'c100'), G.SWIM_IDX.layout + 1).setValue('');
r = call({ action: 'update', race: withImages });
// Το φύλλο ξαναφτιάχτηκε (ο τύπος της ταχύτητας ξανά στη θέση του): μόνο η χαμένη εικόνα λείπει.
assert.deepStrictEqual(Array.from(r.chartsMissing), [chartImages[1].id], 'the lost image is missing');
assert.strictEqual(sh._charts.length, 1, 'a Google chart for the lost image');
assert.strictEqual(sh._images.filter(im => /^swim-chart:/.test(im.alt || '')).length, keys.length - 1);

// 8) Τι δέχεται το Apps Script από το Kinovea.
const ci = G.swimChartImages_([{ id: 'chart-0123abcd', fileId: chartImages[0].fileId, hash: '89abcdef' }, { id: 'chart-x', fileId: chartImages[0].fileId, hash: '89abcdef' },
  { id: 'chart-0123abce', fileId: 'short', hash: '89abcdef' }, { id: 'chart-0123abcf', fileId: chartImages[0].fileId, hash: 'nothex!!' }, null, 'x']);
assert.deepStrictEqual(Object.keys(ci), ['chart-0123abcd']);
assert.deepStrictEqual(JSON.parse(JSON.stringify(G.swimChartImages_('x'))), {});
assert.deepStrictEqual(JSON.parse(JSON.stringify(G.swimSplitSignature_('{"v":8}'))), { grid: '{"v":8}', charts: '' }, 'signature of 2.5');
assert.deepStrictEqual(JSON.parse(JSON.stringify(G.swimSplitSignature_('a\nb'))), { grid: 'a', charts: 'b' });
assert.deepStrictEqual(JSON.parse(JSON.stringify(G.swimA1Box_('G20:I25'))), { r1: 20, c1: 7, r2: 25, c2: 9 });
assert.deepStrictEqual(JSON.parse(JSON.stringify(G.swimA1Box_('AB3'))), { r1: 3, c1: 28, r2: 3, c2: 28 });

// 9) Ο πίνακας των φιναλίστ σε όλο το πλάτος, με ενωμένα κελιά· οι τιμές στο πρώτο κελί κάθε ομάδας.
const g = layout.grid;
let head = 0;
for (let row = 1; row <= g.maxRow; row++) if (g.get(row, 1) && g.get(row, 1).v === 'Φάση' && g.get(row, 2) && g.get(row, 2).v === 'Τιμή') head = row;
assert.ok(head > 0, 'header of the finals table');
const mergedAt = (row, c) => Array.from(g.merges.find(m => m[0] === row && m[1] === c) || []);
assert.deepStrictEqual(mergedAt(head, 2), [head, 2, head, 4], 'Τιμή B-D');
assert.deepStrictEqual(mergedAt(head, 14), [head, 14, head, 15], 'Από τον νικητή N-O');
assert.strictEqual(g.get(head, 14).v, 'Από τον νικητή');
const blocks = layout.inputs.blocks.filter(b => b.r === head + 1);
assert.deepStrictEqual(Array.from(blocks.map(b => b.c)), [1, 2, 5, 7, 8, 10, 12, 14]);
assert.deepStrictEqual(Array.from(blocks.map(b => b.values[0][0])), ['Εκκίνηση', 'Αποκόλληση', '0.68', '4/4', '0.63', '0.70', '0.59', '+0.05']);
assert.ok(g.boxes.some(b => b[0] === head && b[1] === 1 && b[2] === head + 3 && b[3] === 15), 'box A-O');
let width = 0;
for (let c = 2; c <= 4; c++) width += G.swimColumnWidth_(c);
assert.ok(width >= 200, 'room for the label: ' + width);

// 10) Η έκδοση στο φύλλο (στην ενημέρωση, ώστε να φαίνεται στο PDF ποιο script το έφτιαξε).
assert.ok(/έκδοση 2\.9\.8$/.test(String(sh.getRange(2, 11).getValue())), sh.getRange(2, 11).getValue());

console.log('test_charts: ok (' + keys.length + ' charts, finals table row ' + head + ')');

// 11) Ο αγώνας όπως κρατιέται στο ευρετήριο (κανονικοποιημένος) κρατά τα γραφικά και τις εικόνες των γραφημάτων: το PDF από το μενού.
const stored = G.swimStoredRace_('c100');
assert.ok(stored && stored.race, 'stored race');
const again = G.swimNormalizeRace_(stored.race);
assert.strictEqual(Object.keys(again.chartImages).length, keys.length, 'chart images kept in the index');
const gr = G.swimNormalizeRace_({ id: 'x', checkpoints: [{ d: 50, t: 30 }], graphics: { summary: chartImages[0].fileId, versus: 'bad', other: chartImages[0].fileId } }).graphics;
assert.deepStrictEqual(JSON.parse(JSON.stringify(gr)), { summary: chartImages[0].fileId }, 'graphics of the index');
const menu = G.swimUpsertRace_(stored.race);
assert.ok(menu.chartsMissing.length <= 1, 'the menu keeps the images: ' + menu.chartsMissing.length);
console.log('test_charts: stored race ok');
