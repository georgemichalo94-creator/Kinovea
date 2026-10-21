// Τα γραφήματα του PDF (2.9.5). Κάθε γράφημα που τυπώνεται είναι εικόνα του Kinovea, ή γράφημα του Google (ή η εικόνα του) με
// τιμές από κελιά που φαίνονται (ή με τις κρυμμένες γραμμές μέσα) και χωρίς ρυθμίσεις με τις οποίες το Google σχεδιάζει μόνο
// τον τίτλο (τιμές πάνω στις σειρές, περιοχή σε %)· ένα γράφημα χωρίς καμία τιμή δεν μπαίνει ούτε ζητιέται από το Kinovea· οι
// εικόνες για τιμές που άλλαξαν μετά το σχέδιό τους ζητιούνται ξανά από το Kinovea μία φορά πριν από το PDF. Φανταστικά ονόματα.
const { makeContext } = require('./mock_gas');
const assert = require('assert');

const png = Buffer.from([137, 80, 78, 71, 13, 10, 26, 10, 9, 9, 9]).toString('base64');
const cps = [15, 25, 35, 45, 50, 65, 75, 85, 95, 100];
const times = [6.72, 12.48, 18.36, 24.26, 27.42, 36.1, 42.12, 48.36, 54.76, 57.94];
const strokes = [];
for (let t = 5.9; t < 27; t += 1.12) strokes.push(+t.toFixed(2));
for (let t = 33.2; t < 57.6; t += 1.18) strokes.push(+t.toFixed(2));
const finals = {
  label: 'Test final · 100μ. fly Γυναικών', source: 'test',
  swimmers: [1, 2, 3].map(rank => ({ rank, name: 'Finalist ' + rank, times: [{ d: 50, t: 25.4 + rank * 0.2 }, { d: 100, t: 55.4 + rank * 0.5 }] }))
};
const raceOf = (id, t) => ({ id, athlete: 'CHART Erin', athleteId: '940001', gender: 'Γυναικών', style: 'fly', distance: 100, pool: 50,
  competition: 'Chart Open 2026', date: '16.10.2026', round: 'Final', checkpoints: cps.map((d, i) => ({ d, t: (t || times)[i] })), strokes, finals });

// Η τιμή ενός τύπου στο mock: το άθροισμα των χρόνων (στήλη B) που διαβάζει, ώστε να αλλάζει όταν αλλάζει ένας χρόνος.
const fromTimes = (sh, row, col, f) => {
  let s = 0;
  f.replace(/(^|[^A-Z$])B(\d+)/g, (m, pre, n) => { const x = sh._cells[n + ':2']; if (x && typeof x.v === 'number') s += x.v; return m; });
  return s;
};

function setup(opts) {
  const G = makeContext(Object.assign({ athletes: [['unique_id', 'ΟΝΟΜΑΤΕΠΩΝΥΜΟ', 'ΦΥΛΟ'], [940001, 'CHART Erin', 'Γυναικών']], formulaValue: fromTimes }, opts || {}));
  G.swimShowConnectionInfo = () => {};
  G.swimSetup();
  const call = body => JSON.parse(G.swimDoPost({ postData: { contents: JSON.stringify(Object.assign({ token: 'tok', app: 'kinovea-swim' }, body)) } }).text);
  const reports = G._all[Object.keys(G._all).find(k => k !== 'DATA')];
  return { G, call, reports, sheetOf: r => reports.getSheets().find(s => s.getSheetId() === r.sheetId) };
}

// Όπως το Kinovea: σχεδιάζει τα γραφήματα που ζητήθηκαν, τα ανεβάζει ανά 6 και κρατά { id, fileId, hash } για κάθε ένα.
function upload(env, raceId, charts, kept) {
  const out = (kept || []).filter(x => !charts.some(c => c.key === x.id));
  for (let i = 0; i < charts.length; i += 6) {
    const batch = charts.slice(i, i + 6);
    const up = env.call({ action: 'images', raceId, images: batch.map(c => ({ id: c.key, mime: 'image/png', data: png })) });
    assert.ok(up.ok, JSON.stringify(up).slice(0, 200));
    up.files.forEach(f => { const c = batch.find(x => x.key === f.id); if (f.fileId) out.push({ id: f.id, fileId: f.fileId, hash: c.hash }); });
  }
  return out;
}

/**
 * Ό,τι τυπώθηκε στην τελευταία εξαγωγή: { kinovea, google (εικόνες ή γραφήματα του Google), problems }. Ένα γράφημα του Google
 * είναι πρόβλημα όταν σχεδιάζεται μόνο ο τίτλος, όταν δεν έχει καμία τιμή (αριθμό) στις σειρές του ή όταν οι τιμές του είναι σε
 * κρυμμένες γραμμές / στήλες χωρίς να τις δείχνει (SHOW_BOTH).
 */
function printed(G) {
  const exp = G._exports[G._exports.length - 1];
  const kinovea = exp.detail.images.filter(im => /_chart-[0-9a-f]{8}\.png$/.test(im.name || '') && im.row <= exp.r2);
  const google = exp.detail.images.filter(im => im.name === 'chart.png' && im.row <= exp.r2).map(im => Object.assign({ image: true }, im.chart, { blank: im.blank }))
    .concat(exp.detail.charts.map(ch => Object.assign({ image: false }, ch)));
  const problems = [];
  google.forEach(c => problemsOf(c).forEach(x => problems.push(x)));
  return { exp, kinovea: kinovea.length, google: google.length, problems };
}
function problemsOf(c) {
  const out = [];
  if (c.blank || c.titleOnly) out.push(c.title + ': only the title');
  const series = (c.ranges || []).slice(1);
  if (!series.some(rg => rg.values.some(row => row.some(v => typeof v === 'number' && isFinite(v))))) out.push(c.title + ': no values');
  if (c.hidden !== 'SHOW_BOTH' && (c.ranges || []).some(rg => rg.hidden > 0)) out.push(c.title + ': values in hidden cells');
  return out;
}

// ---------------------------------------------------------------------------------------------
// 1) Η ροή του Kinovea με τιμές που αλλάζουν ανάμεσα στο σχέδιο των γραφημάτων και στο PDF (όπως στο PDF της 2.8.0 με 15 από
// τα 17 γραφήματα άδεια): το PDF ζητά ξανά μία φορά όσα άλλαξαν· μετά, όλα εικόνες του Kinovea
// ---------------------------------------------------------------------------------------------
{
  const env = setup({ cache: true });
  const { G, call, sheetOf } = env;
  let r = call({ action: 'update', race: raceOf('p100') });
  assert.ok(r.ok, JSON.stringify(r).slice(0, 200));
  const all = r.chartsMissing.length;
  assert.ok(all >= 10, 'charts: ' + all);
  // Τα γραφήματα του Google στο φύλλο: χωρίς τιμές πάνω στις σειρές, με τις κρυμμένες γραμμές.
  const sh = sheetOf(r);
  assert.strictEqual(sh._charts.length, all);
  sh._charts.forEach(ch => {
    assert.ok(!Object.keys(ch.opts.series || {}).some(k => ch.opts.series[k].dataLabel), 'no labels: ' + ch.opts.title);
    assert.strictEqual(ch.hidden, 'SHOW_BOTH', 'hidden rows too: ' + ch.opts.title);
  });
  // Το PDF: πρώτα τα γραφήματα από το Kinovea.
  const first = call({ action: 'pdf', race: raceOf('p100'), drawCharts: true });
  assert.ok(first.needCharts && first.charts.length === all && !first.pdf, JSON.stringify(first).slice(0, 200));
  let images = upload(env, 'p100', first.charts);
  // Στο μεταξύ άλλαξε ο χρόνος στα 35μ. (π.χ. διόρθωση στο Kinovea): τα γραφήματα με αυτόν δεν είναι πια για τις τιμές τους.
  const t2 = times.slice(); t2[2] = 18.3;
  const third = call({ action: 'pdf', race: Object.assign(raceOf('p100', t2), { chartImages: images }) });
  assert.ok(third.ok && third.needCharts === true && !third.pdf, 'asked again instead of a PDF with Google charts: ' + JSON.stringify(third).slice(0, 300));
  const stale = third.charts.map(c => c.key);
  assert.ok(stale.length > 0 && stale.length < all, 'only the charts whose values changed: ' + stale.length);
  assert.deepStrictEqual(Array.from(third.chartsMissing).sort(), stale.slice().sort());
  assert.ok(G._cache.log.some(x => x[0] === 'put' && /^swimChartsAgain\|/.test(x[1])), 'noted in the cache');
  // Το Kinovea τα ξανασχεδιάζει και ζητά ξανά το PDF: όλα εικόνες του Kinovea.
  images = upload(env, 'p100', third.charts, images);
  const fourth = call({ action: 'pdf', race: Object.assign(raceOf('p100', t2), { chartImages: images }) });
  assert.ok(fourth.ok && fourth.pdf && fourth.pdf.url && !fourth.needCharts, JSON.stringify(fourth).slice(0, 300));
  let p = printed(G);
  assert.deepStrictEqual([p.kinovea, p.google, p.problems], [all, 0, []], 'all images of Kinovea');
  // Ξανά αλλαγή μέσα σε 10 λεπτά: όχι δεύτερη φορά (ποτέ κύκλος χωρίς τέλος)· τα γραφήματα του Google τυπώνονται ολόκληρα.
  const t3 = t2.slice(); t3[7] = 48.3;
  const fifth = call({ action: 'pdf', race: Object.assign(raceOf('p100', t3), { chartImages: images }) });
  assert.ok(fifth.ok && fifth.pdf && !fifth.needCharts, JSON.stringify(fifth).slice(0, 300));
  p = printed(G);
  assert.ok(p.google > 0 && p.kinovea + p.google === all, JSON.stringify([p.kinovea, p.google]));
  assert.deepStrictEqual(p.problems, [], 'every Google chart printed whole, with values');
  assert.ok(p.exp.name.indexOf('_swim_pdf_') === 0, 'from the copy with the first page');
  // Όπως πριν από την 2.9.5 (χωρίς SWIM_PDF_CHARTS): τα γραφήματα του Google με τιμές πάνω στις σειρές, και το Google σχεδιάζει
  // μόνο τον τίτλο τους (το PDF της 2.8.0).
  G.SWIM_PDF_CHARTS = false;
  const t4 = t3.slice(); t4[5] = 36.0;
  call({ action: 'pdf', race: Object.assign(raceOf('p100', t4), { chartImages: images }) });
  p = printed(G);
  assert.ok(p.google > 0 && p.problems.length === p.google && p.problems.every(x => /only the title/.test(x)), JSON.stringify(p.problems));
  G.SWIM_PDF_CHARTS = true;
  // Χωρίς την προσωρινή μνήμη του script: κανένα δεύτερο αίτημα (όπως πριν).
  const noCache = setup();
  noCache.call({ action: 'update', race: raceOf('n100') });
  const a = noCache.call({ action: 'pdf', race: raceOf('n100'), drawCharts: true });
  const imgs = upload(noCache, 'n100', a.charts);
  const b = noCache.call({ action: 'pdf', race: Object.assign(raceOf('n100', t2), { chartImages: imgs }) });
  assert.ok(b.pdf && !b.needCharts, 'no cache: the PDF with what there is');
  assert.deepStrictEqual(printed(noCache.G).problems, []);
}
console.log('test_pdfcharts (1): charts asked again once, Google charts printed whole');

// ---------------------------------------------------------------------------------------------
// 2) Γραφήματα χωρίς καμία τιμή (εδώ: ό,τι βγαίνει από τις χεριές, κενό όπως στο Google χωρίς χεριές): δεν μπαίνουν, δεν
// ζητιούνται από το Kinovea, δεν τυπώνονται· μόλις αποκτήσουν τιμές, μπαίνουν
// ---------------------------------------------------------------------------------------------
{
  const opts = { noStrokes: true };
  const env = setup({ cache: true, formulaValue: (sh, row, col, f) => (opts.noStrokes && /(^|[^A-Z$])H\d/.test(f) ? '' : fromTimes(sh, row, col, f)) });
  const { G, call, sheetOf } = env;
  let r = call({ action: 'update', race: raceOf('e100'), drawCharts: true });
  const race = G.swimNormalizeRace_(raceOf('e100'));
  const layout = G.swimBuildLayout_(race, G.swimAnalyze_(race), Object.assign(G.swimComparisons_(race), { pageHeight: w => G.swimPdfPageHeight_(env.reports, w) }));
  const empty = layout.charts.filter(c => /^(Συχνότητα χεριάς|Μήκος χεριάς DPS)/.test(c.title)).map(c => c.key);
  assert.strictEqual(empty.length, 2, 'stroke rate and length per segment: ' + empty.length);
  assert.ok(empty.every(k => r.chartsMissing.indexOf(k) < 0) && r.charts.every(c => empty.indexOf(c.key) < 0), 'not asked from Kinovea');
  assert.strictEqual(r.chartsMissing.length, layout.charts.length - 2);
  const sh = sheetOf(r);
  const at = new Set(layout.charts.filter(c => empty.indexOf(c.key) >= 0).map(c => c.row + ':' + c.col + ':' + (c.offsetX || 0)));
  assert.ok(!sh._charts.some(ch => at.has(ch.pos[0] + ':' + ch.pos[1] + ':' + ch.pos[2])), 'no Google chart in their place');
  assert.strictEqual(sh._charts.length, layout.charts.length - 2);
  // Το PDF: όλα τα υπόλοιπα από το Kinovea, κανένα κουτί μόνο με τίτλο.
  const first = call({ action: 'pdf', race: raceOf('e100'), drawCharts: true });
  const images = upload(env, 'e100', first.charts);
  const pdf = call({ action: 'pdf', race: Object.assign(raceOf('e100'), { chartImages: images }) });
  assert.ok(pdf.pdf && !pdf.needCharts, JSON.stringify(pdf).slice(0, 200));
  let p = printed(G);
  assert.deepStrictEqual([p.kinovea, p.google, p.problems], [layout.charts.length - 2, 0, []]);
  // Με χεριές: μπαίνουν και ζητιούνται από το Kinovea (μαζί με όσα άλλα άλλαξαν τιμές με τις χεριές).
  opts.noStrokes = false;
  r = call({ action: 'update', race: Object.assign(raceOf('e100'), { chartImages: images }), drawCharts: true });
  assert.ok(empty.every(k => r.chartsMissing.indexOf(k) >= 0), 'now with values: ' + JSON.stringify(r.chartsMissing));
  assert.ok(empty.every(k => r.charts.some(c => c.key === k && c.series.some(s => s.values.some(v => typeof v === 'number')))));
  assert.strictEqual(sh._charts.length, r.chartsMissing.length, 'a Google chart for each until its image');
  assert.ok(layout.charts.filter(c => empty.indexOf(c.key) >= 0).every(c => sh._charts.some(ch => ch.pos[0] === c.row && ch.pos[1] === c.col)), 'in their place');
}
console.log('test_pdfcharts (2): charts without values left out');

// ---------------------------------------------------------------------------------------------
// 3) Κρυμμένες γραμμές στο φύλλο του αγώνα (π.χ. από τον αναλυτή): τα γραφήματα του Google τις δείχνουν (SHOW_BOTH)· και το PDF
// χωρίς την πρώτη σελίδα (χωρίς copyTo): ίδιοι έλεγχοι στο φύλλο του αγώνα
// ---------------------------------------------------------------------------------------------
{
  const env = setup({ copyTo: false });
  const { G, call, sheetOf } = env;
  const r = call({ action: 'update', race: raceOf('h100') });
  const sh = sheetOf(r);
  const race = G.swimNormalizeRace_(raceOf('h100'));
  const T0 = G.swimBuildLayout_(race, G.swimAnalyze_(race), {}).T0;
  sh.hideRows(T0 + 2, 3);
  const pdf = call({ action: 'pdf', race: raceOf('h100') });
  assert.ok(pdf.pdf && pdf.pdf.quick === false, JSON.stringify(pdf.pdf));
  const p = printed(G);
  assert.ok(p.google === r.chartsMissing.length && p.kinovea === 0, JSON.stringify([p.google, p.kinovea]));
  assert.deepStrictEqual(p.problems, []);
  assert.ok(p.exp.detail.images.some(im => im.chart && im.chart.ranges.some(rg => rg.hidden > 0)), 'some chart reads the hidden rows');
  // Χωρίς SHOW_BOTH, το ίδιο γράφημα θα ήταν πρόβλημα (ο έλεγχος το βλέπει)· και ένα χωρίς τιμές, και ένα μόνο με τίτλο.
  const withHidden = p.exp.detail.images.find(im => im.chart && im.chart.ranges.some(rg => rg.hidden > 0)).chart;
  assert.deepStrictEqual(problemsOf(Object.assign({}, withHidden, { hidden: null })), [withHidden.title + ': values in hidden cells']);
  const blankValues = withHidden.ranges.map((rg, i) => Object.assign({}, rg, { values: i ? rg.values.map(row => row.map(() => '')) : rg.values }));
  assert.deepStrictEqual(problemsOf(Object.assign({}, withHidden, { ranges: blankValues })), [withHidden.title + ': no values']);
  assert.deepStrictEqual(problemsOf(Object.assign({}, withHidden, { blank: true })), [withHidden.title + ': only the title']);
  assert.strictEqual(sh._charts.length, r.chartsMissing.length, 'the charts back in the sheet after the PDF');
  const lay = G.swimBuildLayout_(race, G.swimAnalyze_(race), Object.assign(G.swimComparisons_(race), { pageHeight: w => G.swimPdfPageHeight_(env.reports, w) }));
  assert.deepStrictEqual(sh._charts.map(ch => ch.pos.join(':')).sort(), Array.from(lay.charts, c => [c.row, c.col, c.offsetX || 0, 0].join(':')).sort(), 'back in their places');
}
console.log('test_pdfcharts (3): hidden rows, PDF from the race sheet');

// ---------------------------------------------------------------------------------------------
// 4) Ο έλεγχος από το μενού: με τις ρυθμίσεις των reports το γράφημα και η εικόνα του ολόκληρα· με τις τιμές πάνω στη σειρά
// (2.5–2.9.4) μόνο ο τίτλος
// ---------------------------------------------------------------------------------------------
{
  const env = setup();
  const html = env.G.swimChartsCheckHtml_(env.reports);
  assert.ok(/✔ Τα γραφήματα των reports σχεδιάζονται ολόκληρα/.test(html), html);
  assert.ok(/ρυθμίσεις των reports<\/td><td[^>]*>\d+ bytes<\/td><td>✔ ολόκληρο<\/td><td[^>]*>εικόνα: \d+ bytes ✔<\/td>/.test(html), html);
  assert.ok(/τιμές πάνω στη σειρά \(έκδοση 2\.5–2\.9\.4\)<\/td><td[^>]*>\d+ bytes<\/td><td>✖ μόνο ο τίτλος<\/td><td[^>]*>εικόνα: \d+ bytes ✖<\/td>/.test(html), html);
}
console.log('test_pdfcharts (4): the check of the menu');
