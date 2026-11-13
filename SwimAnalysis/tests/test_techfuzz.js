// 2.17.0 (validity check): the technique report of a training video with random fictional analyses: starts, turns or both, one or
// more attempts, values missing at random, angles, snapshots (not uploaded, wide images of a group of screens, with an elite frame),
// evaluations with and without frames, notes, yards, and every comparison (none, the previous analysis, an elite final, an elite
// technique analysis, both). No exception, no undefined / NaN / null in the cells and the values, no overlapping merges, the values
// of each block inside the grid, every image inside the page, whole on one page of the PDF, over empty cells and apart from the
// others, and in English no Greek letter of the script. Fixed seed: the same analyses every time.
const { makeContext } = require('./mock_gas');
const assert = require('assert');

let seed = 20261113;
const rnd = () => (seed = (seed * 1103515245 + 12345) % 2147483648) / 2147483648;
const pick = a => a[Math.floor(rnd() * a.length)];
const chance = p => rnd() < p;
const round = (v, d) => +v.toFixed(d);

const G = makeContext({ athletes: [['unique_id', 'ΟΝΟΜΑΤΕΠΩΝΥΜΟ', 'ΦΥΛΟ'], [600001, 'FUZZ ATHLETE', 'Γυναικών'], [600002, 'FUZZ OTHER', 'Ανδρών'], [600003, 'FUZZ ENGLISH', 'Γυναικών']] });
G.swimShowConnectionInfo = () => {};
G.swimSetup();
const post = body => JSON.parse(G.swimDoPost({ postData: { contents: JSON.stringify(Object.assign({ token: 'tok', app: 'kinovea-swim' }, body)) } }).text);
const reports = G._all[Object.keys(G._all).find(k => k !== 'DATA')];
// The levels of two events (fictional), for the elite of a final.
const levels = reports.getSheetByName('Επίπεδα') || G.swimBenchmarksSheet_(reports);
levels.getRange(2, 1, 2, 13).setValues([
  ['Στροφή τοίχος-15μ.', 'Γυναικών', 'Ελεύθερο', 100, 50, '', 6.45, 6.58, 6.70, 6.88, 7.04, 7.30, 'Fuzz'],
  ['Εκκίνηση 0-15μ.', 'Γυναικών', 'Ελεύθερο', 100, 50, '', 6.02, 6.18, 6.28, 6.42, 6.55, '', 'Fuzz']]);

const TURN_KEYS = ['in5', 'lastStrokeGap', 'lastStrokeDist', 'rotation', 'contact', 'turnTime', 'out5', 'out10', 'out15', 'inOut5', 'inOut10', 'inOut15',
  'v0_5', 'v5_10', 'v10_15', 'underwater', 'kicks', 'kickRate', 'kickDist', 'uwVel', 'breakoutTime', 'breakoutDist', 'bo15Vel', 'firstStroke'];
const START_KEYS = ['reaction', 'handsOff', 'backFoot', 'blockTime', 'blockPhase', 'flight', 'entryTime', 'entryDist', 'underwater', 'kicks', 'kickRate', 'kickDist',
  'uwVel', 'pushVel', 'breakoutTime', 'breakoutDist', 'bo15Vel', 'firstStroke', 't5', 't10', 't15', 'v0_5', 'v5_10', 'v10_15'];
const BASE = { in5: 3.0, lastStrokeGap: 1.1, lastStrokeDist: 1.8, rotation: 0.9, contact: 0.3, turnTime: 1.4, out5: 2.4, out10: 5.4, out15: 8.6, inOut5: 5.4, inOut10: 8.4,
  inOut15: 11.6, v0_5: 2.1, v5_10: 1.6, v10_15: 1.5, underwater: 2.6, kicks: 5, kickRate: 1.9, kickDist: 0.9, uwVel: 1.8, breakoutTime: 2.9, breakoutDist: 6.2,
  bo15Vel: 1.5, firstStroke: 3.1, reaction: 0.15, handsOff: 0.5, backFoot: 0.6, blockTime: 0.75, blockPhase: 0.6, flight: 0.3, entryTime: 1.05, entryDist: 3.1,
  pushVel: 2.5, t5: 1.9, t10: 4.7, t15: 8.0 };
const values = (keys, keep) => {
  const v = {};
  keys.forEach(k => { if (chance(keep)) v[k] = k === 'kicks' ? Math.round(BASE[k] + rnd() * 3 - 1) : round(BASE[k] * (0.9 + rnd() * 0.2), 3); });
  // A value of zero or a string as Kinovea could send them.
  if (chance(0.05) && keys.indexOf('kicks') >= 0) v.kicks = 0;
  return v;
};
// The events of the automatic snapshots and the stages, as Kinovea sends them (in Greek: the report shows them in its language).
const EVENTS = { turn: [['Τελευταία χεριά', 'Προσέγγιση'], ['Αρχή περιστροφής', 'Περιστροφή'], ['Επαφή στον τοίχο', 'Επαφή στον τοίχο'],
  ['Αποκόλληση από τον τοίχο', 'Αποκόλληση'], ['Πρώτος δελφινισμός', 'Υποβρύχια φάση'], ['Έξοδος από το νερό', 'Έξοδος από το νερό'], ['Πρώτη χεριά', 'Κολύμβηση'],
  ['', 'Υποβρύχια φάση'], ['', '']],
  start: [['Σήμα εκκίνησης', 'Θέση ετοιμότητας'], ['Αντίδραση', 'Φάση βατήρα'], ['Χέρια φεύγουν από τον βατήρα', 'Φάση βατήρα'], ['Πίσω πόδι φεύγει', 'Φάση βατήρα'],
    ['Αποδέσμευση από τον βατήρα', 'Αποδέσμευση'], ['Είσοδος στο νερό', 'Είσοδος στο νερό'], ['', 'Πτήση'], ['Πρώτος δελφινισμός', 'Υποβρύχια φάση'],
    ['Έξοδος από το νερό', 'Έξοδος από το νερό'], ['Πρώτη χεριά', 'Κολύμβηση'], ['', '']] };
const SIZES = [[1500, 1000], [1920, 1080], [1280, 720], [3840, 1080], [5760, 1080], [2560, 1080], [0, 0], [undefined, undefined], [1080, 1920]];
const words = (en, n) => Array.from({ length: n }, () => pick(en ? ['push', 'wall', 'knees', 'head', 'tight', 'line', 'late', 'early', 'kick', 'breath']
  : ['ώθηση', 'τοίχος', 'γόνατα', 'κεφάλι', 'σφιχτό', 'ευθεία', 'αργά', 'νωρίς', 'δελφινισμός', 'ανάσα'])).join(' ');

function sheetOf(id, en, scored) {
  const groups = [];
  let n = 0;
  for (let g = 0; g < 1 + Math.floor(rnd() * 4); g++) {
    const items = [];
    for (let i = 0; i < 1 + Math.floor(rnd() * 5); i++) {
      n++;
      items.push({ n: String(n), key: id + '.k' + n, text: words(en, 2 + Math.floor(rnd() * 9)), target: chance(0.3) ? '≤ 0.4 s' : undefined,
        cue: chance(0.5) ? words(en, 3 + Math.floor(rnd() * 12)) : undefined });
    }
    groups.push({ label: words(en, 1 + Math.floor(rnd() * 2)), items });
  }
  return { title: (en ? 'Fuzz sheet ' : 'Φύλλο δοκιμής ') + id, source: 'Fuzz', scored, groups };
}

/** A random training analysis. en: the texts Kinovea sends in English (the report then has no Greek letter of its own). */
function training(id, o) {
  const en = !!o.en;
  const phases = [], angles = [], snapshots = [], evals = [], sheets = {};
  const kinds = o.kinds === 'both' ? ['start', 'turn'] : [o.kinds];
  kinds.forEach(kind => {
    const attempts = o.attempts || (1 + Math.floor(rnd() * 3));
    for (let a = 1; a <= attempts; a++) {
      const pid = id + '-' + kind[0] + a;
      phases.push({ id: pid, kind, attempt: a, values: values(kind === 'turn' ? TURN_KEYS : START_KEYS, o.keep) });
      for (let k = 0; k < Math.floor(rnd() * 3); k++)
        angles.push({ id: pid + '-a' + k, name: pick(en ? ['Knee', 'Hip', 'Custom angle'] : ['Γωνία γονάτων στην επαφή', 'Γόνατο', 'Ισχίο', 'Γωνία εισόδου στο νερό']),
          t: round(rnd() * 3 - 1, 2), deg: round(40 + rnd() * 120, 1), phase: chance(0.85) ? pid : null });
      for (let s = 0; s < Math.floor(rnd() * (o.snaps + 1)); s++) {
        const ev = pick(EVENTS[kind]), size = pick(SIZES);
        const snap = { id: pid + '-s' + s, t: chance(0.9) ? round(rnd() * 5 - 1.5, 2) : null, phase: chance(0.95) ? pid : '', stage: ev[1], event: ev[0],
          comment: chance(0.5) ? words(en, 1 + Math.floor(rnd() * 25)) : '', fileId: chance(0.9) ? 'f-' + pid + '-s' + s : '', w: size[0], h: size[1] };
        if (chance(0.2)) { snap.eliteFileId = 'e-' + pid + '-s' + s; snap.eliteLabel = en ? 'Elite frame' : 'Elite καρέ'; snap.eliteComment = chance(0.5) ? words(en, 6) : ''; }
        snapshots.push(snap);
      }
      if (chance(o.evals)) {
        const variant = chance(0.5) ? 'a' : 'b';
        const sid = 'sheet-' + kind + '-' + variant + (en ? '-en' : '');
        if (!sheets[sid]) sheets[sid] = sheetOf(sid, en, variant === 'a');
        const scores = {};
        sheets[sid].groups.forEach(g => g.items.forEach(it => {
          if (chance(0.15)) return;
          const s = { v: pick([1, 1, 0, 0, -1]) };
          if (chance(0.2)) s.a = 1;
          if (chance(0.3)) s.m = pick(['131°', '0.34 s', '35 cm', '5 · 1.92/s']);
          if (s.v === 0 && chance(0.6)) {
            const size = pick(SIZES);
            s.f = 'err-' + pid + '-' + it.n;
            snapshots.push({ id: s.f, t: round(rnd() * 3, 2), phase: pid, stage: '', event: 'eval:' + it.key, comment: '✗ ' + it.text, fileId: s.f, w: size[0], h: size[1] });
          }
          scores[it.key] = s;
        }));
        evals.push({ id: pid + '-ev', phase: pid, phaseLabel: (en ? (kind === 'turn' ? 'Turn ' : 'Start ') : (kind === 'turn' ? 'Στροφή ' : 'Εκκίνηση ')) + a, phaseKind: kind,
          sheet: sid, date: o.date, notes: chance(0.3) ? words(en, 12) : '', scores,
          level: chance(0.2) ? { label: en ? 'Turn 5 m in + 10 m out' : 'Στροφή 5 μ. μέσα + 10 μ. έξω', value: 9.84, text: en ? '9.84 s · level 4' : '9.84 s · επίπεδο 4', level: 4, levels: 8 } : undefined });
      }
    }
  });
  // The analyses in English of another athlete: "the previous one" of an analysis is then in its language (its comments are shown).
  const who = o.other ? ['FUZZ OTHER', '600002', 'Ανδρών'] : (en ? ['FUZZ ENGLISH', '600003', 'Γυναικών'] : ['FUZZ ATHLETE', '600001', 'Γυναικών']);
  return { id, kind: 'training', athlete: who[0], athleteId: who[1], gender: who[2],
    style: o.style, pool: o.pool, poolUnit: o.yards ? 'yd' : undefined, date: o.date, checkpoints: [], lang: en ? 'en' : undefined,
    tech: { phases, angles, snapshots, values: { notes: chance(0.5) ? words(en, 5 + Math.floor(rnd() * 40)) : '' }, sheets, evals } };
}

const GREEK = /[Ͱ-Ͽἀ-῿]/;
const badText = (s, en) => typeof s === 'string' && (/undefined|NaN|Infinity|\[object/.test(s) || /\bnull\b/.test(s) ||
  (en && GREEK.test(s.replace(/(^|[^Ͱ-Ͽ])Δ(?![Ͱ-Ͽ])/g, '$1'))));

/** The checks of one layout. */
function check(L, what, en) {
  const g = L.grid, W = 1124, page = L.page.height;
  assert.ok(page > 0, what + ': page height');
  // Cells and values.
  Object.values(g.cells).forEach(c => {
    assert.ok(!badText(c.v, en), `${what}: cell ${c.r}:${c.c} «${c.v}»`);
    assert.ok(!(typeof c.v === 'number' && !isFinite(c.v)), `${what}: number of cell ${c.r}:${c.c}`);
    assert.ok(c.c >= 1 && c.c <= 15 && c.r >= 1, `${what}: cell ${c.r}:${c.c} outside the grid`);
  });
  const dynamic = {};
  (L.inputs.blocks || []).forEach(b => {
    assert.ok(b.values.length > 0 && b.r >= 1 && b.c >= 1, `${what}: block ${b.r}:${b.c}`);
    const width = b.values[0].length;
    b.values.forEach((row, i) => {
      assert.strictEqual(row.length, width, `${what}: block ${b.r}:${b.c} row ${i} has ${row.length} values, the first ${width}`);
      assert.ok(b.c + row.length - 1 <= 15, `${what}: block ${b.r}:${b.c} outside the 15 columns`);
      row.forEach((v, j) => {
        assert.ok(v !== undefined && v !== null && !(typeof v === 'number' && !isFinite(v)) && !badText(v, en), `${what}: value ${b.r + i}:${b.c + j} «${v}»`);
        if (v !== '') dynamic[(b.r + i) + ':' + (b.c + j)] = v;
      });
    });
  });
  // Merges.
  const ms = g.merges;
  const ov = (a, b) => !(a[2] < b[0] || b[2] < a[0] || a[3] < b[1] || b[3] < a[1]);
  for (let i = 0; i < ms.length; i++) for (let j = i + 1; j < ms.length; j++) assert.ok(!ov(ms[i], ms[j]), `${what}: overlapping merges ${ms[i]} / ${ms[j]}`);
  // Geometry: the rows on the pages of the PDF.
  const H = {}, top = {}, pageOf = {};
  let y = 0, pg = 0;
  for (let r = 1; r <= L.lastRow + 40; r++) {
    const h = G.swimRowHeight_(g, r);
    assert.ok(h > 0, `${what}: height of row ${r}`);
    if (y > 0 && y + h > page) { pg++; y = 0; }
    H[r] = h; top[r] = y; pageOf[r] = pg; y += h;
  }
  const x0 = [0];
  for (let c = 1; c <= 15; c++) x0.push(x0[c - 1] + G.swimColumnWidth_(c));
  const span = {};
  ms.forEach(m => { span[m[0] + ':' + m[1]] = m; });
  // The cells with something written (as built, or in each update), as rectangles of their page.
  const texts = [];
  const filled = {};
  Object.values(g.cells).forEach(c => { if (c.v !== '' && c.v !== undefined && c.v !== null) filled[c.r + ':' + c.c] = true; });
  Object.keys(dynamic).forEach(k => { filled[k] = true; });
  Object.keys(filled).forEach(k => {
    const r = +k.split(':')[0], c = +k.split(':')[1], m = span[k];
    const r2 = m ? m[2] : r, c2 = m ? m[3] : c;
    texts.push({ page: pageOf[r], x: x0[c - 1], y: top[r], x2: x0[c2], y2: pageOf[r2] === pageOf[r] ? top[r2] + H[r2] : page, key: k });
  });
  const boxes = [];
  const box = (o, kind) => {
    const oy = o.offsetY === undefined ? (kind === 'image' ? 4 : 0) : o.offsetY;
    const x = kind === 'image' ? o.x : x0[o.col - 1] + (o.offsetX || 0);
    assert.ok(o.width > 0 && o.height > 0, `${what}: size of ${kind} ${JSON.stringify(o).slice(0, 160)}`);
    assert.ok(x >= 0 && x + o.width <= W, `${what}: ${kind} outside the page width (${x} + ${o.width})`);
    assert.ok(top[o.row] + oy + o.height <= page, `${what}: ${kind} at row ${o.row} crosses the end of its page (${top[o.row]} + ${oy} + ${o.height} > ${page})`);
    boxes.push({ page: pageOf[o.row], x, y: top[o.row] + oy, x2: x + o.width, y2: top[o.row] + oy + o.height, kind, id: o.fileId || o.title });
  };
  (L.images || []).forEach(im => { assert.ok(im.fileId, `${what}: image without a file`); box(im, 'image'); });
  (L.charts || []).forEach(ch => box(ch, 'chart'));
  const hit = (a, b) => a.page === b.page && a.x < b.x2 - 0.5 && b.x < a.x2 - 0.5 && a.y < b.y2 - 0.5 && b.y < a.y2 - 0.5;
  for (let i = 0; i < boxes.length; i++) {
    for (let j = i + 1; j < boxes.length; j++) assert.ok(!hit(boxes[i], boxes[j]), `${what}: ${boxes[i].kind} ${boxes[i].id} over ${boxes[j].kind} ${boxes[j].id}`);
    texts.forEach(t => assert.ok(!hit(boxes[i], t), `${what}: ${boxes[i].kind} ${boxes[i].id} over the cell ${t.key} «${dynamic[t.key] !== undefined ? dynamic[t.key] : (g.cells[t.key] || {}).v}»`));
  }
  // Charts read cells of the grid.
  (L.charts || []).forEach(ch => (ch.ranges || []).forEach(a => assert.ok(/^[A-O]\d+:[A-O]\d+$/.test(a), `${what}: chart range ${a}`)));
}

const STYLES = ['fs', 'bk', 'br', 'fly'];
let cases = 0, withPrev = 0, withElite = 0, withRef = 0, rebuilt = 0;
const failures = [];
for (let i = 0; i < 140; i++) {
  const en = i % 5 === 4;
  const style = pick(STYLES), pool = pick([25, 50]), yards = chance(0.08);
  const kinds = pick(['turn', 'turn', 'start', 'both']);
  const base = { en, style, pool: yards ? 25 : pool, yards, kinds };
  const id = 'fz' + i;
  try {
    // An earlier analysis of the athlete and an elite technique analysis (of another swimmer), in the Sheet.
    const prevKinds = chance(0.8) ? kinds : pick(['turn', 'start', 'both']);
    let r = post({ action: 'update', lang: en ? 'en' : undefined,
      race: training(id + 'p', Object.assign({}, base, { kinds: prevKinds, keep: 0.4 + rnd() * 0.6, snaps: 5, evals: 0.6, date: '10.10.2026' })) });
    assert.ok(r.ok, 'previous: ' + JSON.stringify(r).slice(0, 300));
    r = post({ action: 'update', lang: en ? 'en' : undefined,
      race: training(id + 'e', Object.assign({}, base, { kinds, keep: 0.5 + rnd() * 0.5, snaps: 4, evals: 0.3, date: '1.9.2026', other: true })) });
    assert.ok(r.ok, 'elite analysis: ' + JSON.stringify(r).slice(0, 300));
    r = post({ action: 'saveTechElite', raceId: id + 'e', name: 'Fuzz elite ' + i });
    assert.ok(r.ok, 'saveTechElite: ' + JSON.stringify(r).slice(0, 300));

    const now = training(id, Object.assign({}, base, { keep: pick([0.15, 0.5, 0.85, 1]), snaps: pick([0, 2, 6]), evals: pick([0, 0.5, 1]), date: '12.11.2026' }));
    const compare = {};
    const mode = i % 7;
    if (mode === 1 || mode === 4 || mode === 5) compare.techPrev = pick(['auto', 'best', id + 'p', 'missing-id']);
    if (mode === 2 || mode === 4) {
      const vt = {}, vs = {};
      ['in5', 'out15', 'inOut15', 'breakoutTime', 'breakoutDist'].forEach(k => { if (chance(0.8)) vt[k] = round(BASE[k] * 0.85, 3); });
      ['blockTime', 't15', 'breakoutTime', 'breakoutDist'].forEach(k => { if (chance(0.8)) vs[k] = round(BASE[k] * 0.85, 3); });
      compare.techElite = { type: 'final', id: 'FZ|F|fs|100|mean', label: en ? 'FZ2030 · 100m fs Women · mean of 8' : 'FZ2030 · 100μ. fs Γυναικών · μέσος όρος 8',
        gender: 'F', style: 'fs', distance: chance(0.8) ? 100 : 0, pool: 50, values: { turn: vt, start: chance(0.9) ? vs : undefined } };
    }
    if (mode === 3 || mode === 5) compare.techElite = { type: 'ref', id: chance(0.9) ? id + 'e' : 'missing-elite', label: chance(0.5) ? 'Fuzz elite ' + i : '' };
    if (mode === 6) compare.techElite = pick([null, 'text', { type: 'final' }, { type: 'ref' }, { values: {} }, { type: 'final', values: { turn: { in5: 'x' } } }]);
    if (Object.keys(compare).length) now.compare = compare;

    const build = () => {
      const rr = G.swimNormalizeRace_(now);
      const extras = { pageHeight: w => G.swimPdfPageHeight_(reports, w), techCompare: G.swimTechCompare_(rr) };
      return { L: G.swimBuildLayout_(rr, G.swimAnalyze_(rr), extras), cmp: extras.techCompare };
    };
    const built = en ? G.swimWithLang_('en', build) : build();
    check(built.L, id + ' (' + kinds + ', mode ' + mode + (en ? ', en' : '') + (yards ? ', yd' : '') + ')', en);
    if (built.cmp.prev) withPrev++;
    if (built.cmp.elite && built.cmp.elite.type === 'final') withElite++;
    if (built.cmp.elite && built.cmp.elite.type === 'ref') withRef++;

    // The whole way, as Kinovea sends it: the report is written, and the same analysis again changes nothing.
    r = post({ action: 'update', race: now, lang: en ? 'en' : undefined });
    assert.ok(r.ok, 'update: ' + JSON.stringify(r).slice(0, 300));
    if (r.rebuilt) rebuilt++;
    const sh = reports.getSheets().find(s => s.getSheetId() === r.sheetId);
    Object.values(sh._cells).forEach(c => assert.ok(!badText(c.v, en), `${id}: the sheet has «${c.v}»`));
    const again = post({ action: 'update', race: now, lang: en ? 'en' : undefined });
    assert.ok(again.ok && !again.rebuilt, id + ': the same analysis again is not rebuilt: ' + JSON.stringify(again).slice(0, 200));
    cases++;
  } catch (e) {
    failures.push(id + ': ' + String(e && e.message ? e.message : e).split('\n')[0].slice(0, 600));
    if (failures.length >= 12) break;
  }
}

assert.strictEqual(failures.length, 0, failures.length + ' failures:\n' + failures.join('\n'));
assert.ok(withPrev > 15 && withElite > 10 && withRef > 10, `comparisons covered: prev ${withPrev}, final ${withElite}, elite analysis ${withRef}`);
console.log(`test_techfuzz: ${cases} random fictional technique analyses (prev ${withPrev}, final ${withElite}, elite analysis ${withRef}), no errors, images whole on their page, English without Greek`);
