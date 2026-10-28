// Η πρώτη σελίδα του PDF «Με μια ματιά» (2.8.0): ακριβώς μία σελίδα πριν από την πλήρη αναφορά, που μένει ίδια και στις ίδιες
// σελίδες· σε αντίγραφο του φύλλου που σβήνεται· στα ελληνικά και στα αγγλικά· σε όλα τα αγωνίσματα· χωρίς copyTo το PDF όπως
// πριν. Και οι αναλύσεις με τους χρόνους τους (σύγκριση στο βίντεο). Φανταστικά ονόματα παντού.
const { makeContext } = require('./mock_gas');
const assert = require('assert');

const GREEK = /[Ͱ-Ͽἀ-῿]/;
const ATHLETES = [['unique_id', 'ΟΝΟΜΑΤΕΠΩΝΥΜΟ', 'ΦΥΛΟ', 'ΕΤΟΣ ΓΕΝΝΗΣΗΣ', 'ΚΑΤΗΓΟΡΙΑ', 'ΣΩΜΑΤΕΙΟ'],
  [920001, 'QUICK Alice', 'Γυναικών', 2007, 'Γυναικών', 'Quick Swim Club'], [920002, 'OTHER Beth', 'Γυναικών', 2006, 'Γυναικών', 'Other Club']];
const jpeg = Buffer.from([0xff, 0xd8, 0xff, 0xe0, 1, 2, 3, 4, 0xff, 0xd9]).toString('base64');
const who = { athlete: 'QUICK Alice', athleteId: '920001', gender: 'Γυναικών', club: 'Quick Swim Club', category: 'Γυναικών', birthYear: '2007',
  competition: 'Quick Open 2026', location: 'Quickville' };
const cps100 = [15, 25, 35, 45, 50, 65, 75, 85, 95, 100];
const t100 = [6.72, 12.48, 18.36, 24.26, 27.42, 36.1, 42.12, 48.36, 54.76, 57.94];
const strokes100 = scale => {
  const out = [];
  for (let t = 5.9; t < 27; t += 1.12) out.push(+(t * scale).toFixed(2));
  for (let t = 33.2; t < 57.6; t += 1.18) out.push(+(t * scale).toFixed(2));
  return out;
};

function setup(opts) {
  const G = makeContext(Object.assign({ athletes: ATHLETES }, opts || {}));
  G.swimShowConnectionInfo = () => {};
  G.swimSetup();
  const call = body => JSON.parse(G.swimDoPost({ postData: { contents: JSON.stringify(Object.assign({ token: 'tok', app: 'kinovea-swim' }, body)) } }).text);
  const reports = G._all[Object.keys(G._all).find(k => k !== 'DATA')];
  const refRows = [];
  cps100.forEach((d, i) => refRows.push([i === 0 ? 'Ref QUICK' : '', i === 0 ? 'Γυναικών' : '', i === 0 ? 'fly' : '', i === 0 ? '100' : '', i === 0 ? '50' : '',
    i === 0 ? 'Quick Final 2025' : '', d, String(+(t100[i] * 0.95).toFixed(2)), '', 4]));
  reports.getSheetByName('Elite αναφορές').getRange(2, 1, refRows.length, 10).setValues(refRows);
  const refs = call({ action: 'references' });
  assert.ok(refs.ok && refs.references.length === 1);
  const final = call({ action: 'image', raceId: 'q-full', snapshotId: 'final', mime: 'image/jpeg', data: jpeg }).fileId;
  return { G, call, reports, reference: refs.references[0], final };
}

function race100(env) {
  return Object.assign({ id: 'q-full', style: 'fly', distance: 100, pool: 50, round: 'Τελικός', date: '16.10.2026', lane: 4,
    checkpoints: cps100.map((d, i) => ({ d, t: t100[i] })), strokes: strokes100(1),
    kicks: [1.5, 2.0, 2.5, 3.0, 28.5, 29.0], breakouts: [5.2, 31.0], feetOff: [0.7, 28.3], entries: [1.02],
    manual: { reaction: 0.66, breakoutDist: 11.8, turns: { 50: { turnTime: 0.85 } } },
    reference: env.reference, compare: { pb: 'auto' }, finalFrame: { fileId: env.final, t: 57.94 } }, who);
}

const pb100 = env => Object.assign({ id: 'q-pb', style: 'fly', distance: 100, pool: 50, round: 'Ημιτελικός', date: '1.6.2026', lane: 3,
  checkpoints: cps100.map((d, i) => ({ d, t: +(t100[i] * 1.02).toFixed(2) })), strokes: strokes100(1.02), reference: env.reference }, who);

/** (2.11.0) Οι γραμμές των σχολίων στο τέλος της πλήρους ανάλυσης (από την κενή γραμμή πριν από τον τίτλο «Σχόλια»), 0 χωρίς αυτά. */
function notesRows(exp) {
  let at = 0;
  Object.keys(exp.detail.cells).forEach(k => {
    const [r, c] = k.split(':').map(Number), v = exp.detail.cells[k].v;
    if (c === 1 && (v === 'Σχόλια' || v === 'Comments') && r > at) at = r;
  });
  return at ? exp.r2 - at + 2 : 0;
}

/** Η τελευταία εξαγωγή: η πρώτη σελίδα (γραμμές και κείμενα) και η αναφορά. */
function lastExport(G, report) {
  const exp = G._exports[G._exports.length - 1];
  const tail = notesRows(exp);
  const n = exp.r2 - report.print.rows - tail;
  const texts = [];
  Object.keys(exp.detail.cells).forEach(k => {
    const r = +k.split(':')[0], v = exp.detail.cells[k].v;
    if (r <= n && typeof v === 'string' && v) texts.push(v);
  });
  let height = 0;
  for (let r = 1; r <= n; r++) height += exp.detail.rowHeights[r];
  return { exp, n, texts, height, tail };
}

// ---------------------------------------------------------------------------------------------
// 1) 100μ. πεταλούδα με PB, αναφορά και τελικό καρέ: η πρώτη σελίδα, και η αναφορά μία σελίδα πιο κάτω
// ---------------------------------------------------------------------------------------------
{
  const env = setup({ imagesStay: true });
  const { G, call, reports } = env;
  assert.ok(call({ action: 'update', race: pb100(env) }).ok);
  const before = G._exports.length;
  const r = call({ action: 'pdf', race: race100(env) });
  assert.ok(r.ok && r.pdf && r.pdf.url && r.pdf.name === r.sheetName + '.pdf', JSON.stringify(r).slice(0, 300));
  assert.strictEqual(r.quick, undefined, 'the first page is not sent back');
  assert.strictEqual(r.pdf.quick, true, 'the PDF says that it starts with the first page');
  assert.ok(G._exports.length > before);
  const q = lastExport(G, r);
  assert.ok(/^_swim_pdf_\d+$/.test(q.exp.name), q.exp.name);
  assert.ok(!reports.getSheets().some(s => /^_swim_pdf_\d+$/.test(s.getName())), 'the copy is removed');
  const H = q.exp.H;
  assert.ok(q.height <= H && q.height >= H - 8, 'one page: ' + q.height + ' of ' + H);
  assert.strictEqual(q.exp.detail.pageStarts[1], q.n + 1, 'the report starts on page 2');
  // Η αναφορά όπως την τυπώνει το φύλλο της: οι ίδιες σελίδες.
  const sheet = reports.getSheets().find(s => s.getSheetId() === r.sheetId);
  const starts = [1];
  let y = 0;
  for (let row = 1; row <= r.print.rows; row++) {
    const h = sheet._rowHeights[row] || 21;
    if (y > 0 && y + h > H) { starts.push(row); y = h; } else y += h;
  }
  assert.deepStrictEqual(Array.from(q.exp.detail.pageStarts).slice(1, starts.length + 1), starts.map(s => s + q.n), 'same page breaks one page later');
  // Οι εικόνες της αναφοράς κατέβηκαν (εδώ το «Google» δεν τις κατεβάζει μόνο του: τις κατεβάζει το script).
  sheet._images.forEach(im => assert.ok(q.exp.detail.images.some(x => x.row === im.row + q.n && x.col === im.col), 'image moved: ' + im.row));
  assert.ok(q.exp.detail.images.some(x => x.row <= q.n), 'the final frame on the first page');
  // Τα κείμενα: τίτλος, αθλητής, χρόνος, PB, αναφορά, βασικά σημεία, πίνακας.
  const has = t => assert.ok(q.texts.indexOf(t) >= 0, 'missing: ' + t + '\n' + q.texts.slice(0, 80).join(' | '));
  const like = re => assert.ok(q.texts.some(t => re.test(t)), 'missing: ' + re + '\n' + q.texts.join(' | '));
  has('ΜΕ ΜΙΑ ΜΑΤΙΑ'); has('Βασικά σημεία'); has('Ο τερματισμός'); has('Χρόνοι ανά σημείο ελέγχου'); has('57.94');
  has('ΣΕ ΣΧΕΣΗ ΜΕ ΤΟ PB'); has('ΣΕ ΣΧΕΣΗ ΜΕ ΤΗΝ ΑΝΑΦΟΡΑ');
  like(/^QUICK Alice {2}\(Διάδρομος 4\)$/);
  like(/^Νέο ατομικό ρεκόρ: -1\.16 από τον καλύτερο χρόνο του αθλητή \(59\.10, Ημιτελικός · Quick Open 2026 · 1\.6\.2026\)\.$/);
  like(/^\+2\.90 από την αναφορά Ref QUICK \(55\.04\) · η μεγαλύτερη διαφορά στο 50–100μ\. \(\+1\.53\)\.$/);
  like(/^1ο μισό 27\.42 · 2ο μισό 30\.52 \(\+3\.10\)\.$/);
  like(/^15μ\. σε 6\.72 \(\+0\.34 από την αναφορά\) · αντίδραση 0\.66 · έξοδος από το νερό στα 11\.8μ\. · 4 υποβρύχιοι δελφινισμοί\.$/);
  like(/^Στροφή \(-5\/\+15\) στα 50μ\.: /);
  has('Τελευταία 5μ.'); has('3.18'); has('11.84 · στροφή 0.88');
  like(/^=SPARKLINE\(\{[0-9.]+\},\{"charttype","bar";"max",[0-9.]+;"color1","#[0-9A-F]{6}"\}\)$/);
  like(/^Ακολουθεί η πλήρης ανάλυση του αγώνα · Swim Analysis 2\.13\.0$/);
  ['15–25', '45–50', '95–100'].forEach(has);
  // 2.9.5: ο μέσος όρος του ΔΑΚ και από κάτω κάθε 50άρι· ο ΔΑΚ κάθε 50άρι και στον πίνακα.
  has('ΔΑΚ (μ.ο.)'); has('ΔΑΚ 50άρι'); like(/^1ο \d+\.\d\d · 2ο \d+\.\d\d$/);
  // Το φύλλο του αγώνα δεν έχει τίποτα από την πρώτη σελίδα.
  assert.ok(!Object.keys(sheet._cells).some(k => sheet._cells[k].v === 'ΜΕ ΜΙΑ ΜΑΤΙΑ'), 'the report sheet is unchanged');

  // ---- Στα αγγλικά: κανένα ελληνικό γράμμα (εκτός από το Δ) στην πρώτη σελίδα.
  const en = call({ action: 'pdf', lang: 'en', race: race100(env) });
  assert.ok(en.ok && en.pdf, JSON.stringify(en).slice(0, 200));
  const qe = lastExport(G, en);
  const greek = qe.texts.filter(t => GREEK.test(t.replace(/(^|[^Ͱ-Ͽ])Δ(?![Ͱ-Ͽ])/g, '$1')));
  assert.deepStrictEqual(greek, [], 'Greek left on the English first page');
  const hasEn = t => assert.ok(qe.texts.indexOf(t) >= 0, 'missing: ' + t + '\n' + qe.texts.join(' | '));
  hasEn('AT A GLANCE'); hasEn('Key points'); hasEn('Times per checkpoint'); hasEn('VS PB');
  // 2.9.5: ο ΔΑΚ κάθε 50άρι (πλακίδιο και πίνακας) στα αγγλικά.
  hasEn('SPI (mean)'); hasEn('SPI of the 50');
  assert.ok(qe.texts.some(t => /^1st \d+\.\d\d · 2nd \d+\.\d\d$/.test(t)), 'SPI of each 50 under the mean');
  assert.ok(qe.texts.some(t => /^New personal best: -1\.16 from the best time of the swimmer \(59\.10, Semi-final · Quick Open 2026 · 1\.6\.2026\)\.$/.test(t)),
    qe.texts.join(' | '));
  assert.ok(qe.texts.some(t => /^15m in 6\.72 \(\+0\.34 from the reference\) · reaction 0\.66/.test(t)));
  console.log('test_quick (1): one page (' + q.n + ' rows, ' + q.height + '/' + H + ' px), the report one page later, English without Greek');

  // ---- Οι αναλύσεις του αθλητή με τους χρόνους τους, και οι αναλύσεις άλλων αθλητών (2.8.0).
  const other = Object.assign({}, race100(env), { id: 'q-other', athlete: 'OTHER Beth', athleteId: '920002', compare: {},
    checkpoints: cps100.map((d, i) => ({ d, t: +(t100[i] * 0.99).toFixed(2) })), finalFrame: null });
  assert.ok(call({ action: 'update', race: other }).ok);
  const list = call({ action: 'analyses', query: { athleteId: '920001', athlete: 'QUICK Alice', style: 'fly', distance: 100, pool: 50, poolUnit: 'm',
    raceId: 'q-full', others: true, gender: 'Γυναικών' } });
  assert.ok(list.ok && list.analyses.length === 1 && list.analyses[0].id === 'q-pb', JSON.stringify(list).slice(0, 400));
  assert.deepStrictEqual(list.analyses[0].times.map(t => t.d), cps100);
  assert.strictEqual(list.analyses[0].times[9].t, 59.1);
  assert.ok(list.others.length === 1 && list.others[0].id === 'q-other' && list.others[0].athlete === 'OTHER Beth', JSON.stringify(list.others));
  assert.strictEqual(list.others[0].times.length, 10);
  // Χωρίς others: όπως πριν (μόνο οι αναλύσεις του αθλητή).
  assert.strictEqual(call({ action: 'analyses', query: { athleteId: '920001', style: 'fly', distance: 100, pool: 50, raceId: 'x' } }).others, undefined);
  // Άλλο φύλο: όχι στη λίστα.
  assert.strictEqual(call({ action: 'analyses', query: { athleteId: '920001', style: 'fly', distance: 100, pool: 50, raceId: 'q-full', others: true, gender: 'Ανδρών' } }).others.length, 0);
  console.log('test_quick (2): analyses with their times, and other swimmers of the event');
}

// ---------------------------------------------------------------------------------------------
// 1β) Οι αναλύσεις άλλων αθλητών στην προσωρινή μνήμη του script: μία ανάγνωση ανά αγώνισμα, φύλο και πλήθος αναλύσεων
// ---------------------------------------------------------------------------------------------
{
  const env = setup({ cache: true });
  const { G, call } = env;
  const mk = (id, athlete, athleteId, k) => Object.assign({}, race100(env), { id, athlete, athleteId: athleteId, compare: {}, finalFrame: null,
    checkpoints: cps100.map((d, i) => ({ d, t: +(t100[i] * k).toFixed(2) })) });
  assert.ok(call({ action: 'update', race: mk('c-alice', 'QUICK Alice', '920001', 1) }).ok);
  assert.ok(call({ action: 'update', race: mk('c-beth', 'OTHER Beth', '920002', 0.99) }).ok);
  const ask = (athleteId, athlete) => call({ action: 'analyses', query: { athleteId, athlete, style: 'fly', distance: 100, pool: 50, poolUnit: 'm',
    raceId: 'none', others: true, gender: 'Γυναικών' } });
  const reads = () => G._cache.log.filter(x => x[0] === 'put').length;
  const a1 = ask('920001', 'QUICK Alice');
  assert.deepStrictEqual(a1.others.map(o => o.id), ['c-beth']);
  assert.strictEqual(reads(), 1, 'the list is made once');
  const a2 = ask('920001', 'QUICK Alice');
  assert.deepStrictEqual(a2.others, a1.others, 'the same list from the cache');
  assert.strictEqual(reads(), 1, 'from the cache');
  // Ο άλλος αθλητής από την ίδια μνήμη: οι αναλύσεις της πρώτης, όχι οι δικές του.
  const b = ask('920002', 'OTHER Beth');
  assert.deepStrictEqual(b.others.map(o => o.id), ['c-alice']);
  assert.strictEqual(reads(), 1, 'one list for every athlete of the event');
  assert.strictEqual(b.others[0].times.length, 10);
  // Νέα ανάλυση στο αγώνισμα: νέα λίστα, μαζί της.
  assert.ok(call({ action: 'update', race: mk('c-cleo', 'THIRD Cleo', '920003', 0.97) }).ok);
  const a3 = ask('920001', 'QUICK Alice');
  assert.deepStrictEqual(a3.others.map(o => o.id), ['c-cleo', 'c-beth'], 'fastest first');
  assert.strictEqual(reads(), 2, 'a new list after a new analysis');
  // Άλλη πισίνα (25μ.): όχι οι αναλύσεις των 50μ. της μνήμης.
  const pool25 = call({ action: 'analyses', query: { athleteId: '920001', athlete: 'QUICK Alice', style: 'fly', distance: 100, pool: 25, poolUnit: 'm',
    raceId: 'none', others: true, gender: 'Γυναικών' } });
  assert.deepStrictEqual(pool25.others, [], 'another pool: ' + JSON.stringify(pool25.others).slice(0, 100));
  // Μια ανάλυση που ξαναστάλθηκε με άλλους χρόνους: η μνήμη ξαναγίνεται (όχι οι παλιοί χρόνοι).
  assert.ok(call({ action: 'update', race: mk('c-beth', 'OTHER Beth', '920002', 0.96) }).ok);
  const a4 = ask('920001', 'QUICK Alice');
  const beth = a4.others.find(o => o.id === 'c-beth');
  assert.ok(beth && Math.abs(beth.times[9].t - +(t100[9] * 0.96).toFixed(2)) < 1e-9, JSON.stringify(beth && beth.times[9]));
  assert.deepStrictEqual(a4.others.map(o => o.id), ['c-beth', 'c-cleo'], 'the new times order them');
  // Η μνήμη κρατά μόνο ό,τι επιστρέφεται (και τα κλειδιά του αθλητή για να βγαίνουν οι δικές του).
  const lastKey = G._cache.log.filter(x => x[0] === 'put').pop()[1];
  const stored = G.swimCacheGet_(G.CacheService.getScriptCache(), lastKey);
  assert.ok(Array.isArray(stored) && stored.length === 3 && stored.every(x => x.id && x.times && 'athleteId' in x), JSON.stringify(stored).slice(0, 200));
  assert.ok(/\|fly\|100\|50\|m\|/.test(lastKey.replace(/\|[^|]*Πεταλούδα[^|]*\|/, '|fly|')) || /\|100\|50\|m\|/.test(lastKey), 'pool in the key: ' + lastKey);
  // Σε κομμάτια όταν δεν χωρά σε μία τιμή (π.χ. 1500μ.: 150 χρόνοι η καθεμία): ίδια λίστα πίσω.
  G.SWIM_CACHE_PART = 700;
  const before = G._cache.log.length;
  G.swimCachePut_(G.CacheService.getScriptCache(), 'swimEvent|test', stored, 60);
  const parts = Number(G._cache.store['swimEvent|test']);
  assert.ok(parts > 1 && G._cache.log.slice(before).some(x => x[0] === 'putAll' && x[1] === parts), 'parts ' + parts);
  assert.deepStrictEqual(G.swimCacheGet_(G.CacheService.getScriptCache(), 'swimEvent|test'), stored);
  // Ένα κομμάτι που χάθηκε (η μνήμη το έσβησε): null, η λίστα ξαναγίνεται.
  delete G._cache.store['swimEvent|test|1'];
  assert.strictEqual(G.swimCacheGet_(G.CacheService.getScriptCache(), 'swimEvent|test'), null);
  // Πολύ μεγάλη για τη μνήμη (πάνω από 40 κομμάτια): δεν μπαίνει, χωρίς σφάλμα.
  G.SWIM_CACHE_PART = 10;
  G.swimCachePut_(G.CacheService.getScriptCache(), 'swimEvent|huge', stored, 60);
  assert.strictEqual(G._cache.store['swimEvent|huge'], undefined);
  G.SWIM_CACHE_PART = 45000;
  console.log('test_quick (2b): the other swimmers of the event from the script cache (in parts when long)');
}

// ---------------------------------------------------------------------------------------------
// 2) Όλα τα αγωνίσματα: η πρώτη σελίδα χωρά πάντα σε μία σελίδα (με λιγότερα όπου χρειάζεται)
// ---------------------------------------------------------------------------------------------
{
  const env = setup();
  const { G, call } = env;
  const make = (id, style, distance, pool, unit) => {
    const cps = [];
    for (let w = 0; w < distance; w += pool) {
      const marks = pool === 50 ? [15, 25, 35, 45, 50] : [15, 20, 25];
      marks.forEach(p => cps.push(w + p));
    }
    const v = style === 'fs' ? 1.75 : 1.55;
    const times = cps.map((d, i) => +(d / v + (d > pool ? 0.6 * Math.floor((d - 1) / pool) : 0) + (i % 3) * 0.01).toFixed(2));
    const strokes = []; for (let t = 5; t < times[times.length - 1] - 0.5; t += 0.68) strokes.push(+t.toFixed(2));
    return Object.assign({ id, style, distance, pool, poolUnit: unit || 'm', round: 'Τελικός', date: '2.7.2026',
      checkpoints: cps.map((d, i) => ({ d, t: times[i] })), strokes, manual: { reaction: 0.7 } }, who);
  };
  const cases = [['q50', 'fs', 50, 50], ['q200', 'im', 200, 50], ['q400', 'fs', 400, 50], ['q800', 'fs', 800, 50], ['q1500', 'fs', 1500, 50],
    ['q100s', 'fly', 100, 25], ['q1650y', 'fs', 1650, 25, 'yd'], ['q400s', 'im', 400, 25]];
  cases.forEach(([id, style, distance, pool, unit]) => {
    const r = call({ action: 'pdf', race: make(id, style, distance, pool, unit) });
    assert.ok(r.ok && r.pdf, id + ': ' + JSON.stringify(r).slice(0, 200));
    const q = lastExport(G, r);
    assert.ok(/^_swim_pdf_\d+$/.test(q.exp.name), id + ' ' + q.exp.name);
    assert.ok(q.height <= q.exp.H && q.height >= q.exp.H - 8, id + ': ' + q.height + ' / ' + q.exp.H);
    assert.strictEqual(q.exp.detail.pageStarts[1], q.n + 1, id + ': the report starts on page 2');
    const title = distance <= 100 ? 'Χρόνοι ανά σημείο ελέγχου' : (distance <= 400 ? 'Χρόνοι ανά 50' : 'Χρόνοι ανά 100');
    assert.ok(q.texts.some(t => t.indexOf(title) === 0), id + ': ' + title);
    // Η πρώτη σελίδα (καθαρή συνάρτηση): το επίπεδο που χώρεσε.
    const race = G.swimNormalizeRace_(make(id, style, distance, pool, unit));
    const analysis = G.swimAnalyze_(race);
    const built = G.swimBuildQuickLook_(race, analysis, {}, {}, { height: q.exp.H, cols: r.print.cols });
    assert.ok(built && built.total === Math.floor(q.exp.H - G.SWIM_QUICK_SLACK), id + ': exact height ' + (built && built.total));
    console.log('  ' + id + ': ' + q.n + ' rows, level ' + built.level + ', ' + built.data.segments.length + ' segments, ' + built.data.points.length + ' points');
  });
  // Προπόνηση: χωρίς πρώτη σελίδα.
  const training = Object.assign({ id: 'q-training', kind: 'training', style: 'fs', pool: 25, date: '10.10.2026', checkpoints: [],
    tech: { phases: [{ id: 'st1', kind: 'start', attempt: 1, label: 'Εκκίνηση 1', values: { reaction: 0.2, t15: 6.1 } }], angles: [], snapshots: [], values: {} } }, who);
  const rt = call({ action: 'pdf', race: training });
  assert.ok(rt.ok && rt.pdf);
  assert.strictEqual(G._exports[G._exports.length - 1].name, rt.sheetName, 'training: the PDF as before');
  console.log('test_quick (3): every event fits on one page; training without it');
}

// ---------------------------------------------------------------------------------------------
// 3) Apps Script χωρίς copyTo (ή αποτυχία του αντιγράφου): το PDF ακριβώς όπως πριν
// ---------------------------------------------------------------------------------------------
{
  const env = setup({ copyTo: false });
  const { G, call } = env;
  const r = call({ action: 'pdf', race: race100(env) });
  assert.ok(r.ok && r.pdf);
  const exp = G._exports[G._exports.length - 1];
  assert.strictEqual(exp.name, r.sheetName);
  assert.strictEqual(exp.r2, r.print.rows);
  // Ένα αντίγραφο χωρίς τις εικόνες της αναφοράς: το PDF όπως πριν (και το αντίγραφο σβήνεται).
  const env2 = setup({ copyImages: false });
  const r2 = env2.call({ action: 'pdf', race: race100(env2) });
  assert.ok(r2.ok && r2.pdf);
  const exp2 = env2.G._exports[env2.G._exports.length - 1];
  assert.strictEqual(exp2.name, r2.sheetName, 'copy without images: the PDF as before');
  assert.ok(!env2.reports.getSheets().some(s => /^_swim_pdf_\d+$/.test(s.getName())), 'the copy is removed');
  assert.strictEqual(r.pdf.quick, false);
  assert.strictEqual(r2.pdf.quick, false);
  // Η εξαγωγή του αντιγράφου αποτυγχάνει: το PDF όπως πριν, από το ίδιο το φύλλο (και το αντίγραφο σβήνεται).
  const env3 = setup({ failExport: /^_swim_pdf_\d+$/ });
  const r3 = env3.call({ action: 'pdf', race: race100(env3) });
  assert.ok(r3.ok && r3.pdf && r3.pdf.quick === false, JSON.stringify(r3).slice(0, 300));
  const exp3 = env3.G._exports[env3.G._exports.length - 1];
  assert.strictEqual(exp3.name, r3.sheetName, 'failed export of the copy: the PDF as before');
  assert.strictEqual(exp3.r2, r3.print.rows);
  assert.ok(!env3.reports.getSheets().some(s => /^_swim_pdf_\d+$/.test(s.getName())), 'the copy is removed');
  // Ό,τι μπήκε προσωρινά για το PDF στο φύλλο του αγώνα (εικόνες των γραφημάτων) έφυγε.
  const sheet3 = env3.reports.getSheetByName(r3.sheetName);
  assert.ok(!sheet3._images.some(im => im.blob && im.blob._name === 'chart.png'), 'no chart images left in the race sheet');
  // Ένα άλλο φύλλο με όνομα που αρχίζει όπως τα αντίγραφα δεν σβήνεται.
  const keep = env3.reports.insertSheet('_swim_pdf_0x1');
  env3.call({ action: 'pdf', race: race100(env3) });
  assert.ok(env3.reports.getSheets().indexOf(keep) >= 0, 'another sheet with the same start of name stays');
  console.log('test_quick (4): without copyTo, a copy without the images, or a failed export of the copy: the PDF as before');
}

// ---------------------------------------------------------------------------------------------
// 4) Τα βασικά σημεία ως καθαρή συνάρτηση: λίγα δεδομένα, πιο αργό από το PB, προειδοποιήσεις
// ---------------------------------------------------------------------------------------------
{
  const G = makeContext({});
  const race = G.swimNormalizeRace_(Object.assign({ id: 'p', style: 'fs', distance: 50, pool: 50, checkpoints: [{ d: 15, t: 6.5 }, { d: 25, t: 6.4 }, { d: 50, t: 24.1 }] }, who));
  const analysis = G.swimAnalyze_(race);
  const slower = G.swimNormalizeRace_(Object.assign({}, race, { checkpoints: [{ d: 15, t: 6.4 }, { d: 25, t: 11.0 }, { d: 50, t: 23.9 }] }));
  const d = G.swimQuickLookData_(race, analysis, { pb: { id: 'x', label: 'PB', race: slower, analysis: G.swimAnalyze_(slower) } }, {});
  assert.ok(d.points.some(p => p.tag === 'PB' && p.tone === 'bad' && /^\+0\.20 από τον καλύτερο χρόνο του αθλητή \(23\.90, /.test(p.text)), JSON.stringify(d.points));
  assert.ok(d.points.some(p => p.tag === 'ΕΛΕΓΧΟΣ' && p.tone === 'bad'), 'warnings point');
  assert.strictEqual(d.mode, 'cp');
  const empty = G.swimQuickLookData_(G.swimNormalizeRace_({ id: 'e', style: 'fs', distance: 100, pool: 50, checkpoints: [{ d: 50 }, { d: 100 }] }),
    G.swimAnalyze_(G.swimNormalizeRace_({ id: 'e', style: 'fs', distance: 100, pool: 50, checkpoints: [{ d: 50 }, { d: 100 }] })), {}, {});
  assert.strictEqual(empty.points.length, 0);
  assert.strictEqual(G.swimQuickDelta_(-0.004), '±0.00');
  assert.strictEqual(G.swimQuickDelta_(1.235), '+1.24');
  console.log('test_quick (5): key points');
}
console.log('QUICK LOOK OK');
