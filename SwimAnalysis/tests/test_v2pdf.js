// 2.11.0: τα μέρη του PDF (σύντομη / πλήρης / και οι δύο· 2.12.0: και σε δύο χωριστά αρχεία), τα σχόλια στο τέλος της πλήρους, η σύντομη και τα σχόλια στα αγγλικά,
// και η μία συχνότητα του φύλλου (η StrRate: από τους χρόνους των χεριών όπου οι χεριές είναι αυτές των χρόνων, αλλιώς χεριές ÷
// χρόνο). Φανταστικά ονόματα παντού.
const { makeContext } = require('./mock_gas');
const assert = require('assert');

const GREEK = /[Ͱ-Ͽἀ-῿]/;
const ATHLETES = [['unique_id', 'ΟΝΟΜΑΤΕΠΩΝΥΜΟ', 'ΦΥΛΟ', 'ΕΤΟΣ ΓΕΝΝΗΣΗΣ', 'ΚΑΤΗΓΟΡΙΑ', 'ΣΩΜΑΤΕΙΟ'],
  [950001, 'SHORT Clara', 'Γυναικών', 2005, 'Γυναικών', 'Short Swim Club']];
const who = { athlete: 'SHORT Clara', athleteId: '950001', gender: 'Γυναικών', club: 'Short Swim Club', category: 'Γυναικών', birthYear: '2005',
  competition: 'Short Open 2026', location: 'Shortville' };
const cps = [15, 25, 35, 45, 50, 65, 75, 85, 95, 100];
const tRace = [7.30, 13.95, 20.62, 27.40, 30.61, 38.21, 45.25, 52.50, 59.92, 63.42];
const tPb = [7.38, 14.00, 20.70, 27.45, 30.70, 38.57, 45.62, 52.85, 60.25, 63.95];
const tRef = [7.10, 13.70, 20.35, 27.05, 30.20, 37.75, 44.70, 51.80, 59.05, 63.00];
const every = (from, to, dt) => { const out = []; for (let t = from; t <= to + 1e-9; t += dt) out.push(+t.toFixed(2)); return out; };
const near = (a, b, eps) => Math.abs(a - b) < (eps || 1e-6);
// Objects of the script are of another realm (the VM of the mock): compared as JSON.
const same = (a, b, msg) => assert.strictEqual(JSON.stringify(a), JSON.stringify(b), msg);

function setup() {
  const G = makeContext({ athletes: ATHLETES });
  G.swimShowConnectionInfo = () => {};
  G.swimSetup();
  const call = body => JSON.parse(G.swimDoPost({ postData: { contents: JSON.stringify(Object.assign({ token: 'tok', app: 'kinovea-swim' }, body)) } }).text);
  const reports = G._all[Object.keys(G._all).find(k => k !== 'DATA')];
  const refRows = cps.map((d, i) => [i === 0 ? 'Ref SHORT' : '', i === 0 ? 'Γυναικών' : '', i === 0 ? 'fs' : '', i === 0 ? '100' : '',
    i === 0 ? '50' : '', i === 0 ? 'Short Final 2025' : '', d, String(tRef[i]), '', (d === 15 || d === 65) ? 7 : ((d === 50 || d === 100) ? 4 : 11)]);
  reports.getSheetByName('Elite αναφορές').getRange(2, 1, refRows.length, 10).setValues(refRows);
  const refs = call({ action: 'references' });
  assert.ok(refs.ok && refs.references.length === 1, JSON.stringify(refs).slice(0, 200));
  return { G, call, reference: refs.references[0] };
}
function race(env, id, times, every1, every2, extra) {
  return Object.assign({ id, style: 'fs', distance: 100, pool: 50, round: 'Τελικός', date: '25.10.2026', lane: 4,
    checkpoints: cps.map((d, i) => ({ d, t: times[i] })), strokes: [].concat(every(5.95, 30.3, every1), every(35.8, 63.0, every2)),
    kicks: [1.5, 2.0, 2.5, 3.0, 32.0, 32.5], breakouts: [5.94, 35.70], feetOff: [0.58, 31.53], entries: [1.06],
    manual: { reaction: 0.58, breakoutDist: 12.4, turns: { 50: { turnTime: 0.92, breakoutDist: 11.2 } } } }, who, extra || {});
}

const env = setup();
const { G, call } = env;
assert.ok(call({ action: 'update', race: race(env, 'v2-pb', tPb, 0.63, 0.65) }).ok);
const main = race(env, 'v2-race', tRace, 0.62, 0.66, { reference: env.reference, compare: { pb: 'v2-pb' } });

// ---- 1) Τα μέρη του PDF: από το αίτημα, αλλιώς από τη ρύθμιση του script (SWIM_PDF_PARTS), αλλιώς η πλήρης όπως ως τώρα.
same(G.swimPdfParts_({ pdfParts: { short: true, full: false } }), { short: true, full: false });
same(G.swimPdfParts_({ pdfParts: { short: true, full: true } }), { short: true, full: true });
same(G.swimPdfParts_({ pdfParts: { short: false, full: false } }), { short: false, full: true }, 'nothing chosen: the full');
same(G.swimPdfParts_(null), { short: false, full: true }, 'an old Kinovea: the full as until now');

// ---- 2) Το PDF για κάθε επιλογή: η σύντομη (3 σελίδες) πρώτη, τα σχόλια μόνο στην πλήρη χωρίς τη σύντομη.
const pdfOf = parts => {
  const r = call({ action: 'pdf', race: main, pdfParts: parts });
  assert.ok(r.ok && r.pdf && r.pdf.url, JSON.stringify(r).slice(0, 300));
  return r.pdf;
};
const pShort = pdfOf({ short: true, full: false });
assert.strictEqual(pShort.short, true, 'short made');
assert.strictEqual(pShort.quick, true);
assert.ok(!('notes' in pShort), 'no comments in the short analysis');
const pFull = pdfOf({ short: false, full: true });
assert.ok(!('short' in pFull), 'the full alone: no short flag');
assert.strictEqual(pFull.quick, true, 'the first page «Με μια ματιά» before the full analysis');
assert.strictEqual(pFull.notes, true, 'comments at the end of the full analysis');
const pBoth = pdfOf({ short: true, full: true });
assert.strictEqual(pBoth.short, true);
assert.ok(!('notes' in pBoth), 'both: the short analysis already says it, no comments again');

const pagesOf = parts => {
  const stored = G.swimStoredRace_('v2-race');
  return G.swimUpsertRace_(stored.race, { parts, pdf: true }).quick;
};
const qShort = pagesOf({ short: true, full: false });
assert.ok(qShort.v2 && !qShort.v1 && !qShort.notes, 'short: the 3 pages only');
assert.strictEqual(qShort.v2.pages, 3);
const H = G.swimPdfPageHeight_(G.swimReportsSpreadsheet_(), 1124);
assert.strictEqual(qShort.v2.total, 3 * (H - 4), 'exactly three pages');
const qFull = pagesOf({ short: false, full: true });
assert.ok(qFull.v1 && !qFull.v2 && qFull.notes, 'full: first page + report + comments');
const qBoth = pagesOf({ short: true, full: true });
assert.ok(qBoth.v2 && !qBoth.v1 && !qBoth.notes, 'both: short pages first, then the report');

// The comments: a title, what went well, what to watch, the explanation, all on one page, bullets with numbers of this race.
const texts = q => Object.keys(q.grid.cells).map(k => q.grid.cells[k].v).filter(v => typeof v === 'string' && v);
const notes = texts(qFull.notes);
['Σχόλια', 'Τι έγινε καλά', 'Τι να προσέξουμε', 'Επεξήγηση'].forEach(t => assert.ok(notes.indexOf(t) >= 0, 'comments: ' + t));
assert.ok(notes.some(t => /^•\s+Νέο ατομικό ρεκόρ: 0\.53 πιο γρήγορα/.test(t)), 'the new PB in what went well');
assert.ok(notes.some(t => /«Ref SHORT»/.test(t)), 'the reference by its name');
const notesTotal = qFull.notes.heights.reduce((a, b) => a + b, 0);
assert.ok(notesTotal <= 2 * H, 'comments fit after the report: ' + notesTotal);

// ---- 3) Η μία συχνότητα: στον πίνακα των χρόνων η StrRate (από τους χρόνους ή τις χεριές), το μήκος κύκλου στη στήλη M.
const nr = G.swimNormalizeRace_(G.swimStoredRace_('v2-race').race);
const an = G.swimAnalyze_(nr);
const layout = G.swimBuildLayout_(nr, an, Object.assign(G.swimComparisons_(nr), { pageHeight: () => H }));
const g = layout.grid, T0 = layout.T0;
const header = c => (g.get(T0 - 1, c) || {}).v;
assert.strictEqual(header(9), 'StrRate (Cycles/min)');
assert.strictEqual(header(13), 'Μήκος κύκλου SL (m/κύκλο)');
assert.strictEqual(header(14), 'Στυλ');
assert.ok(!Object.keys(g.cells).some(k => /SR από χρόνους χεριών/.test(String(g.cells[k].v))), 'no second rate column');
const r25 = an.rows.find(r => r.d === 25);
assert.ok(r25.srTimes && near(r25.sr, 60 / 1.24, 1e-6), 'rate from the stroke times: ' + r25.sr);
const f25 = g.get(T0 + an.rows.indexOf(r25), 9).v;
assert.ok(f25.indexOf('ABS(H' + (T0 + 1) + '-' + r25.strokesAuto + ')<=0.25') > 0 && f25.indexOf(String(G.swimFixed_(r25.srExact, 4))) > 0, f25);
const plan = G.swimInputPlan_(nr, an, layout);
const srCol = plan.columns.find(c => c.col === 9);
assert.ok(srCol && srCol.values[1] === f25 && srCol.values[0] === '', 'the plan writes the rate formulas');
// The rate of the 50s and of the race from the column I (one rate), weighted with the time.
const p50 = g.get(layout.per50.first, 5).v;
assert.ok(/I\d+\*K\d+/.test(p50), 'per 50 from the rates: ' + p50.slice(0, 80));
assert.ok(near(an.blocks[0].sr, 60 / 1.24, 1e-6) && near(an.blocks[1].sr, 60 / 1.32, 1e-6), 'per 50: ' + an.blocks.map(b => b.sr));
// A count corrected by the analyst: strokes ÷ time, in the analysis and in the formula of the sheet.
const fixed = G.swimAnalyze_(G.swimNormalizeRace_(Object.assign({}, main, { segmentStrokes: { 25: 12 } }))).rows.find(r => r.d === 25);
assert.ok(!fixed.srTimes && near(fixed.sr, 12 / 2 * 60 / (13.95 - 7.30), 1e-9), 'corrected: ' + fixed.sr);
// The short analysis says the same rates as the table.
const v2t = texts(qShort.v2);
assert.ok(v2t.some(t => /έπεσε από 48\.4 σε 45\.5 κύκλους\/λεπτό/.test(t)), 'the rates of the short analysis');

// ---- 4) Στα αγγλικά: η σύντομη ανάλυση, τα σχόλια και τα γραφήματά τους χωρίς ελληνικά.
G.SWIM_LANG_ = 'en';
try {
  const stored = G.swimStoredRace_('v2-race');
  const en = G.swimUpsertRace_(stored.race, { parts: { short: true, full: true }, pdf: true }).quick;
  const enFull = G.swimUpsertRace_(stored.race, { parts: { short: false, full: true }, pdf: true }).quick;
  // Δ is the sign of a difference in both languages.
  const greek = list => list.filter(t => GREEK.test(t.replace(/Δ/g, '')));
  same(greek(texts(en.v2)), [], 'short analysis in English');
  same(greek(texts(enFull.notes)), [], 'comments in English');
  const chartTexts = [];
  en.v2.charts.forEach(c => {
    const d = c.data;
    [d.title, d.hTitle, d.vTitle].concat(d.categories || [], (d.series || []).map(s => s.name), (d.groups || []).map(x => x.label))
      .forEach(t => { if (typeof t === 'string') chartTexts.push(t); });
  });
  same(greek(chartTexts), [], 'charts of the short analysis in English');
  assert.ok(texts(enFull.notes).indexOf('Comments') >= 0 && texts(en.v2).some(t => /Where time was gained and lost/.test(t)));
} finally {
  G.SWIM_LANG_ = 'el';
}

// ---- 5) 2.12.0: η σύντομη και η πλήρης ανάλυση σε δύο χωριστά αρχεία («σε δύο αρχεία»).
same(G.swimPdfParts_({ pdfParts: { short: true, full: true, separate: true } }), { short: true, full: true, separate: true });
same(G.swimPdfParts_({ pdfParts: { short: true, full: false, separate: true } }), { short: true, full: false }, 'one part: one file');
const ex0 = G._exports.length;
const two = call({ action: 'pdf', race: main, pdfParts: { short: true, full: true, separate: true }, pdfData: true });
assert.ok(two.ok && two.pdf && two.pdfShort, JSON.stringify(two).slice(0, 300));
assert.strictEqual(G._exports.length, ex0 + 2, 'two PDF exports');
const [exFull, exShort] = G._exports.slice(ex0);
assert.ok(/ - Σύντομη ανάλυση\.pdf$/.test(two.pdfShort.name) && two.pdfShort.name !== two.pdf.name, two.pdfShort.name);
assert.strictEqual(two.pdf.name, two.sheetName + '.pdf', 'the full analysis keeps the name of the report');
assert.ok(two.pdfShort.url && two.pdfShort.id && two.pdfShort.url !== two.pdf.url, 'its own file');
// The full analysis as when it is asked alone (first page, report, comments); the short alone (its three pages).
assert.strictEqual(two.pdf.quick, true);
assert.strictEqual(two.pdf.notes, true, 'the comments at the end of the full analysis');
assert.ok(!('short' in two.pdf), 'no short pages in the full file');
assert.strictEqual(two.pdfShort.short, true);
assert.ok(!('notes' in two.pdfShort), 'no comments in the short file');
assert.strictEqual(exShort.r2 - exShort.r1, qShort.v2.heights.length, 'the short file prints the three pages only');
assert.ok(exFull.r2 - exFull.r1 > layout.print.rows, 'the full file prints the first page and the whole report: ' + exFull.r2 + ' / ' + layout.print.rows);
// The package gets both files; the KOE App and the summary the full analysis.
assert.ok(two.pdf.data && two.pdfShort.data && two.pdf.data !== two.pdfShort.data, 'the two PDFs for the package');
assert.ok(!two.pdfShortFailed);
const one = call({ action: 'pdf', race: main, pdfParts: { short: true, full: true }, pdfData: true });
assert.ok(one.ok && one.pdf && !one.pdfShort, 'without «separate»: one PDF as in 2.11.0');
assert.strictEqual(one.pdf.short, true);

console.log('pages', qShort.v2.total, 'notes', notesTotal, 'rate 25', r25.sr.toFixed(2), 'corrected', fixed.sr.toFixed(2));
console.log('ALL V2 PDF TESTS PASSED');
