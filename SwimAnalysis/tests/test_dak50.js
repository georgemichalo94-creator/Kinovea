// Ο ΔΑΚ κάθε 50άρι (2.9.5), όχι μόνο ο μέσος όρος του: στη στήλη L των χρόνων (στη θέση της «Μέση ταχύτητα από εκκίνηση») στη
// γραμμή όπου τελειώνει κάθε 50άρι, κάτω από τον μέσο όρο στο πλακίδιο του ΔΑΚ, και στην πρώτη σελίδα του PDF («Με μια ματιά»):
// στο πλακίδιο και στον πίνακα. Στα ελληνικά και στα αγγλικά· χωρίς SWIM_DAK50 όπως πριν. Φανταστικά ονόματα.
const { makeContext } = require('./mock_gas');
const assert = require('assert');

const GREEK = /[Ͱ-Ͽἀ-῿]/;
const G = makeContext({ athletes: [['unique_id', 'ΟΝΟΜΑΤΕΠΩΝΥΜΟ', 'ΦΥΛΟ'], [930001, 'FIFTY Dana', 'Γυναικών']] });
G.swimShowConnectionInfo = () => {};
G.swimSetup();
const call = body => JSON.parse(G.swimDoPost({ postData: { contents: JSON.stringify(Object.assign({ token: 'tok', app: 'kinovea-swim' }, body)) } }).text);
const reports = G._all[Object.keys(G._all).find(k => k !== 'DATA')];
const sheetOf = r => reports.getSheets().find(s => s.getSheetId() === r.sheetId);

// Αγώνες σε πισίνα 50μ.: σημεία ελέγχου 15, 25, 35, 45, 50 σε κάθε μήκος, χεριές κάθε ~1,2 s της καθαρής κολύμβησης.
function raceOf(id, dist) {
  const cps = [];
  for (let w = 0; w < dist; w += 50) [15, 25, 35, 45, 50].forEach(p => cps.push(w + p));
  const t = d => +(d / 1.72 + Math.floor((d - 1) / 50) * 0.35).toFixed(2);
  const strokes = [];
  for (let w = 0; w < dist; w += 50) for (let s = t(w + 12); s < t(w + 50) - 0.3; s += 1.2) strokes.push(+s.toFixed(2));
  return { id, athlete: 'FIFTY Dana', athleteId: '930001', gender: 'Γυναικών', style: 'fly', distance: dist, pool: 50, round: 'Τελικός',
    date: '16.10.2026', competition: 'Fifty Open 2026', checkpoints: cps.map(d => ({ d, t: t(d) })), strokes };
}
const build = raw => {
  const race = G.swimNormalizeRace_(raw), an = G.swimAnalyze_(race), layout = G.swimBuildLayout_(race, an, {});
  const rowOf = {};
  race.checkpoints.forEach((c, i) => { rowOf[c.d] = layout.T0 + i; });
  return { race, an, layout, g: layout.grid, rowOf };
};
// Οι αναφορές σε κελιά της στήλης L (A1) σε έναν τύπο.
const refsL = f => { const out = []; const re = /(^|[^A-Z$])L(\d+)/g; let m; while ((m = re.exec(f))) out.push(+m[2]); return out; };

// ---------------------------------------------------------------------------------------------
// 1) 100μ.: η στήλη L των χρόνων με τον ΔΑΚ κάθε 50άρι, το πλακίδιο με τον μέσο όρο και από κάτω τα δύο 50άρια
// ---------------------------------------------------------------------------------------------
let { race, an, layout, g, rowOf } = build(raceOf('d100', 100));
const T0 = layout.T0, lastTiming = T0 + race.checkpoints.length - 1, p = layout.per50;
assert.strictEqual(g.get(T0 - 1, 12).v, 'ΔΑΚ 50άρι', 'header of column L');
assert.ok(!Object.values(g.cells).some(c => c.v === 'Μέση ταχύτητα από εκκίνηση (m/s)'), 'the average speed from the start is gone');
assert.ok(p && p.last - p.first + 1 === 2, JSON.stringify(p));
[50, 100].forEach((d, i) => {
  const x = p.first + i, c = g.get(rowOf[d], 12);
  assert.strictEqual(c.v, '=IF(H' + x + '="","",H' + x + ')', 'the ΔΑΚ of the 50 at ' + d);
  assert.strictEqual(g.get(x, 8).v, G.swimDakFormula_(race, rowOf, d - 50, false), 'H of the per-50 table is the ΔΑΚ');
  assert.ok(c.bg === G.SWIM_COLORS.kpiDak && c.bold && c.color === G.SWIM_COLORS.dakText && c.fmt === G.SWIM_FMT.vel, JSON.stringify(c));
});
race.checkpoints.filter(c => c.d % 50).forEach(c => assert.strictEqual(g.get(rowOf[c.d], 12).v, '', 'nothing at ' + c.d));
// Κανένας τύπος και κανένα γράφημα δεν διάβαζε τη στήλη L των χρόνων: μόνο το πλακίδιο του ΔΑΚ τη διαβάζει τώρα.
Object.values(g.cells).forEach(c => {
  if (typeof c.v !== 'string' || c.v.charAt(0) !== '=') return;
  const inTiming = refsL(c.v).filter(r => r >= T0 && r <= lastTiming);
  if (inTiming.length) assert.ok(c.r === 10 && c.c === 1, 'only the tile reads column L of the times: ' + c.r + ':' + c.c + ' ' + c.v);
});
layout.charts.forEach(ch => ch.ranges.forEach(a1 => {
  const b = G.swimA1Box_(a1);
  assert.ok(!(b.c1 <= 12 && 12 <= b.c2 && b.r1 <= lastTiming && b.r2 >= T0), 'no chart on column L of the times: ' + ch.title + ' ' + a1);
}));
// Το πλακίδιο: ο μέσος όρος (όπως πριν) και από κάτω κάθε 50άρι από τη στήλη L.
assert.strictEqual(g.get(8, 1).v, 'ΔΑΚ (μ.ο.)');
assert.strictEqual(g.get(9, 1).v, '=IFERROR(AVERAGE(H' + p.first + ':H' + p.last + '),"")', 'the mean as before');
const L50 = 'L' + rowOf[50], L100 = 'L' + rowOf[100];
assert.strictEqual(g.get(10, 1).v, '=IF(COUNT(' + L50 + ',' + L100 + ')=0,"Δείκτης Απόδοσης Κολύμβησης (μ.ο. 50άρι)",' +
  'IF(ISNUMBER(' + L50 + '),"1ο "&TEXT(' + L50 + ',"0.00"),"1ο –")&" · "&IF(ISNUMBER(' + L100 + '),"2ο "&TEXT(' + L100 + ',"0.00"),"2ο –"))');
assert.strictEqual(g.get(10, 1).wrap, true);
// Το ύψος της γραμμής χωρά το κείμενο που θα δείξει ο τύπος (μετρά για μία γραμμή) και το κείμενο χωρίς τιμές.
const width = G.swimColumnWidth_(1) + G.swimColumnWidth_(2);
assert.ok(G.swimRowHeight_(g, 10) >= G.swimTextHeight_('1ο 0.00 · 2ο 0.00', width, 8, false));
assert.ok(G.swimRowHeight_(g, 10) >= G.swimTextHeight_('Δείκτης Απόδοσης Κολύμβησης (μ.ο. 50άρι)', width, 8, false));
// 11 (2.11.0): και μία στήλη συχνότητας· κάθε report ξαναφτιάχνεται μία φορά.
assert.strictEqual(JSON.parse(layout.signature).v, 11, 'every report is rebuilt once with the new columns');

// ---------------------------------------------------------------------------------------------
// 2) 200μ.: τέσσερα 50άρια κάτω από τον μέσο όρο (δύο γραμμές)· 400μ.: οκτώ, μόνο στους πίνακες· 50μ.: ένα, όπως πριν
// ---------------------------------------------------------------------------------------------
({ race, g, rowOf, layout } = build(raceOf('d200', 200)));
assert.strictEqual(g.get(8, 1).v, 'ΔΑΚ (μ.ο.)');
assert.deepStrictEqual(refsL(g.get(10, 1).v).filter((v, i, a) => a.indexOf(v) === i), [50, 100, 150, 200].map(d => rowOf[d]), 'the four 50s');
assert.ok(/"4ο "&TEXT/.test(g.get(10, 1).v));
// Δύο 50άρια σε κάθε γραμμή: η αλλαγή γραμμής στον τύπο, και ύψος για δύο γραμμές με ΔΑΚ δύο ψηφίων.
assert.ok(/"2ο –"\)&CHAR\(10\)&IF\(ISNUMBER\(L\d+\),"3ο "/.test(g.get(10, 1).v), g.get(10, 1).v);
const four = '1ο 88.88 · 2ο 88.88\n3ο 88.88 · 4ο 88.88';
assert.strictEqual(G.swimTextLines_(four, width - 5, G.swimFontSize_(8) * 4 / 3, false), 2, 'two lines: ' + four);
assert.ok(G.swimRowHeight_(g, 10) >= G.swimTextHeight_(four, width, 8, false), 'room for two lines');
({ race, g, rowOf, layout } = build(raceOf('d400', 400)));
assert.strictEqual(g.get(8, 1).v, 'ΔΑΚ');
assert.strictEqual(g.get(10, 1).v, 'Δείκτης Απόδοσης Κολύμβησης (μ.ο. 50άρι)', 'eight 50s: in the tables');
for (let k = 1; k <= 8; k++) assert.strictEqual(g.get(rowOf[50 * k], 12).v, '=IF(H' + (layout.per50.first + k - 1) + '="","",H' + (layout.per50.first + k - 1) + ')');
({ race, g, rowOf, layout } = build(raceOf('d50', 50)));
assert.strictEqual(g.get(8, 1).v, 'ΔΑΚ');
assert.strictEqual(g.get(10, 1).v, 'Δείκτης Απόδοσης Κολύμβησης (μ.ο. 50άρι)', 'one 50: the mean is the 50');
assert.strictEqual(g.get(rowOf[50], 12).v, '=IF(H' + layout.per50.first + '="","",H' + layout.per50.first + ')');

// ---------------------------------------------------------------------------------------------
// 3) Χωρίς SWIM_DAK50: όπως πριν (η μέση ταχύτητα από την εκκίνηση, το πλακίδιο, η υπογραφή)
// ---------------------------------------------------------------------------------------------
G.SWIM_DAK50 = false;
({ race, g, rowOf, layout } = build(raceOf('d100', 100)));
assert.strictEqual(g.get(layout.T0 - 1, 12).v, 'Μέση ταχύτητα από εκκίνηση (m/s)');
race.checkpoints.forEach(c => assert.strictEqual(g.get(rowOf[c.d], 12).v, '=IF(B' + rowOf[c.d] + '="","",A' + rowOf[c.d] + '/B' + rowOf[c.d] + ')'));
assert.strictEqual(g.get(8, 1).v, 'ΔΑΚ');
assert.strictEqual(g.get(10, 1).v, 'Δείκτης Απόδοσης Κολύμβησης (μ.ο. 50άρι)');
assert.strictEqual(JSON.parse(layout.signature).v, 9);
assert.strictEqual(G.swimQuickLookData_(race, G.swimAnalyze_(race), {}, null).dak50.length, 0);
G.SWIM_DAK50 = true;

// ---------------------------------------------------------------------------------------------
// 4) Στα αγγλικά: «SPI of the 50», «SPI (mean)», «1st … · 2nd …», χωρίς ελληνικά· τα τακτικά αριθμητικά
// ---------------------------------------------------------------------------------------------
G.swimWithLang_('en', () => {
  const b = build(raceOf('d100', 100));
  assert.strictEqual(b.g.get(b.layout.T0 - 1, 12).v, 'SPI of the 50');
  assert.strictEqual(b.g.get(8, 1).v, 'SPI (mean)');
  const sub = b.g.get(10, 1).v;
  assert.ok(/"Swimming Performance Index \(average of the 50s\)"/.test(sub) && /"1st "&TEXT/.test(sub) && /"2nd "&TEXT/.test(sub), sub);
  assert.ok(!GREEK.test(sub), 'no Greek: ' + sub);
  assert.deepStrictEqual([1, 2, 3, 4, 11, 12, 13, 21, 22, 23, 101, 111].map(G.swimOrdinal_),
    ['1st', '2nd', '3rd', '4th', '11th', '12th', '13th', '21st', '22nd', '23rd', '101st', '111th']);
});
assert.deepStrictEqual([1, 2, 3, 12].map(G.swimOrdinal_), ['1ο', '2ο', '3ο', '12ο']);
assert.strictEqual(G.swimDak50Text_([{ k: 1, v: 1.234 }, { k: 2, v: null }, { k: 3, v: 10.5 }]), '1ο 1.23 · 2ο – · 3ο 10.50');
console.log('test_dak50 (1-4): column L of the times, the tile, without the switch, English');

// ---------------------------------------------------------------------------------------------
// 5) «Με μια ματιά»: στο πλακίδιο ο μέσος όρος και από κάτω τα δύο 50άρια· στον πίνακα ανά σημείο ελέγχου ο ΔΑΚ στη γραμμή
// όπου τελειώνει κάθε 50άρι· ανά 50 κάθε 50άρι· οι τιμές του φύλλου (με όσα γράφτηκαν με το χέρι), αλλιώς της ανάλυσης
// ---------------------------------------------------------------------------------------------
const cellsOf = q => Object.values(q.grid.cells);
const textAt = (q, r, c) => { const x = q.grid.get(r, c); return x ? x.v : undefined; };
const level = mode => ({ points: 7, image: 0, mode, strip: true });
{
  const raw = raceOf('d100', 100), race1 = G.swimNormalizeRace_(raw), an1 = G.swimAnalyze_(race1);
  const daks = an1.blocks.map(b => b.dak);
  assert.ok(daks.length === 2 && daks.every(v => v > 0), JSON.stringify(daks));
  let q = G.swimQuickLayout_(race1, an1, {}, null, 15, level('cp'), 0);
  const want = '1ο ' + daks[0].toFixed(2) + ' · 2ο ' + daks[1].toFixed(2);
  const cap = cellsOf(q).find(c => c.v === 'ΔΑΚ (μ.ο.)');
  assert.ok(cap, 'tile caption');
  assert.strictEqual(textAt(q, cap.r + 2, cap.c), want, 'the two 50s under the mean');
  assert.strictEqual(textAt(q, cap.r + 1, cap.c), q.data.dak.toFixed(2), 'the mean');
  // Ο πίνακας: η κεφαλίδα και ο ΔΑΚ στις γραμμές 45–50 και 95–100, κενό στις άλλες.
  const head = cellsOf(q).find(c => c.v === 'ΔΑΚ 50άρι');
  assert.ok(head && head.c === 15, 'header of the table');
  const rowOfLabel = l => cellsOf(q).find(c => c.c === 1 && c.v === l).r;
  assert.strictEqual(textAt(q, rowOfLabel('45–50'), 15), daks[0].toFixed(2));
  assert.strictEqual(textAt(q, rowOfLabel('95–100'), 15), daks[1].toFixed(2));
  ['0–15', '15–25', '50–65', '85–95'].forEach(l => assert.strictEqual(textAt(q, rowOfLabel(l), 15), '', l));
  // Οι τιμές του φύλλου προηγούνται (π.χ. διορθωμένες με το χέρι).
  q = G.swimQuickLayout_(race1, an1, {}, { dak50: [{ k: 1, v: 9.99 }, { k: 2, v: 8.5 }] }, 15, level('cp'), 0);
  assert.strictEqual(textAt(q, cap.r + 2, cap.c), '1ο 9.99 · 2ο 8.50');
  assert.strictEqual(textAt(q, rowOfLabel('95–100'), 15), '8.50');
  // Στα αγγλικά.
  G.swimWithLang_('en', () => {
    const qe = G.swimQuickLayout_(race1, an1, {}, null, 15, level('cp'), 0);
    const ce = cellsOf(qe).find(c => c.v === 'SPI (mean)');
    assert.ok(ce, 'English caption');
    assert.strictEqual(textAt(qe, ce.r + 2, ce.c), '1st ' + daks[0].toFixed(2) + ' · 2nd ' + daks[1].toFixed(2));
    assert.ok(cellsOf(qe).some(c => c.v === 'SPI of the 50'), 'English header');
  });
}
{
  // 200μ. (ανά 50): στο πλακίδιο δεν χωρούν τέσσερα· στον πίνακα κάθε 50άρι με την τιμή του φύλλου.
  const race2 = G.swimNormalizeRace_(raceOf('d200', 200)), an2 = G.swimAnalyze_(race2);
  const sheetDak = an2.blocks.map((b, i) => ({ k: i + 1, v: 5 + i }));
  const q = G.swimQuickLayout_(race2, an2, {}, { dak50: sheetDak }, 15, level('50'), 0);
  const cap = cellsOf(q).find(c => c.v === 'ΔΑΚ');
  assert.ok(cap && textAt(q, cap.r + 2, cap.c) === 'μ.ο. 50άρι', 'four 50s do not fit in the tile');
  ['0–50', '50–100', '100–150', '150–200'].forEach((l, i) => {
    const r = cellsOf(q).find(c => c.c === 1 && c.v === l).r;
    assert.strictEqual(textAt(q, r, 15), (5 + i).toFixed(2), l);
  });
}

// ---------------------------------------------------------------------------------------------
// 6) Από το Kinovea ως το PDF: η στήλη L στο φύλλο, οι τιμές του πίνακα «Ανά 50» για την πρώτη σελίδα
// ---------------------------------------------------------------------------------------------
{
  const raw = raceOf('d100', 100);
  let r = call({ action: 'update', race: raw });
  assert.ok(r.ok && r.rebuilt, JSON.stringify(r).slice(0, 200));
  const sh = sheetOf(r);
  const race1 = G.swimNormalizeRace_(raw), an1 = G.swimAnalyze_(race1);
  // Το layout όπως το φτιάχνει το Apps Script (με το ύψος της σελίδας του PDF).
  const lay = G.swimBuildLayout_(race1, an1, Object.assign(G.swimComparisons_(race1), { pageHeight: w => G.swimPdfPageHeight_(reports, w) }));
  const T = lay.T0, at = d => T + race1.checkpoints.findIndex(c => c.d === d);
  assert.strictEqual(sh.getRange(T - 1, 12).getValue(), 'ΔΑΚ 50άρι');
  assert.strictEqual(sh.getRange(at(50), 12).getValue(), '=IF(H' + lay.per50.first + '="","",H' + lay.per50.first + ')');
  assert.strictEqual(sh.getRange(at(15), 12).getValue(), '');
  // Ο ΔΑΚ κάθε 50άρι από το φύλλο: οι αριθμοί του πίνακα (εδώ ένας γραμμένος με το χέρι), αλλιώς της ανάλυσης.
  sh.getRange(lay.per50.first, 8).setValue(7.25);
  const k50 = G.swimReadKpis_(sh, lay, an1).dak50;
  assert.deepStrictEqual(JSON.parse(JSON.stringify(k50)), [{ k: 1, v: 7.25 }, { k: 2, v: an1.blocks[1].dak }]);
  // Το PDF: η πρώτη σελίδα με το πλακίδιο του ΔΑΚ (ο μέσος όρος και κάθε 50άρι) και η πλήρης αναφορά με τη στήλη L.
  r = call({ action: 'pdf', race: raw });
  assert.ok(r.ok && r.pdf && r.pdf.quick === true, JSON.stringify(r.pdf));
  const exp = G._exports[G._exports.length - 1];
  const texts = Object.keys(exp.detail.cells).map(k => exp.detail.cells[k].v).filter(v => typeof v === 'string');
  assert.ok(texts.indexOf('ΔΑΚ (μ.ο.)') >= 0, 'the tile of the first page');
  assert.ok(texts.some(v => /^1ο \d+\.\d\d · 2ο \d+\.\d\d$/.test(v)), 'each 50 on the first page');
  assert.ok(texts.indexOf('ΔΑΚ 50άρι') >= 0 && texts.indexOf('Μέση ταχύτητα από εκκίνηση (m/s)') < 0, 'the column of the report');
}
console.log('test_dak50 (5-6): the first page of the PDF and the sheet');
