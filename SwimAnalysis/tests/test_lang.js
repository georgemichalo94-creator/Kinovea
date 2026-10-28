// Το report στα αγγλικά (2.7.7): με lang 'el' (ή χωρίς lang) ακριβώς όπως το script πριν από τα αγγλικά, με 'en' χωρίς
// ελληνικά (εκτός από όσα έγραψε ο αναλυτής), οι τιμές του Kinovea στα ελληνικά στη γλώσσα του report (swimL_), η γλώσσα
// μόνο για το αίτημα που τη ζητά. Φανταστικά ονόματα παντού.
const fs = require('fs');
const path = require('path');
const assert = require('assert');
const { execFileSync } = require('child_process');
const { makeContext } = require('./mock_gas');

const ROOT = path.join(__dirname, '..', '..');
const FILE = 'SwimAnalysis/GoogleSheet/SwimRaceAnalysis.gs';
const CURRENT = fs.readFileSync(path.join(ROOT, FILE), 'utf8');
const versionOf = src => /var SWIM_VERSION = '([^']+)'/.exec(src)[1];

// Το script πριν από τα αγγλικά: του HEAD ή, όταν τα αγγλικά είναι ήδη σε commit, το τελευταίο χωρίς SWIM_EN_.
function originalSource() {
  const git = args => execFileSync('git', ['-C', ROOT].concat(args), { encoding: 'utf8', maxBuffer: 1 << 26 });
  const commits = ['HEAD'].concat(git(['log', '--format=%H', '--', FILE]).split('\n').filter(Boolean));
  for (const c of commits) {
    const src = git(['show', c + ':' + FILE]);
    if (src.indexOf('var SWIM_EN_') < 0) return src;
  }
  throw new Error('no script without the English report in the history');
}
const ORIGINAL = originalSource();
// 2.9.8: η μέση ταχύτητα της υποβρύχιας φάσης με το όνομα του πού αρχίζει (εκκίνηση: η είσοδος· στροφή: η αποκόλληση), όπως
// στο νέο script· οι νέες ταχύτητες της εξόδου (pushVel, bo15Vel) δεν έχουν τιμές στις αναλύσεις αυτού του ελέγχου.
const UW_OLD = "  { key: 'uwVel', label: 'Μέση ταχύτητα υποβρύχιας φάσης', unit: 'v', kind: 'both', best: 'max' },\n";
const UW_NEW = "  { key: 'uwVel', label: 'Μέση ταχύτητα υποβρύχιας φάσης (είσοδος → έξοδος)', unit: 'v', kind: 'start', best: 'max' },\n" +
  "  { key: 'uwVel', label: 'Μέση ταχύτητα αποκόλληση → έξοδος', unit: 'v', kind: 'turn', best: 'max' },\n";
assert.ok(ORIGINAL.indexOf(UW_OLD) > 0 && CURRENT.indexOf(UW_OLD) < 0, 'the 2.9.8 names of the underwater speed');
// 2.11.0: μία στήλη συχνότητας, η StrRate: από τους χρόνους των χεριών όπου οι χεριές του τμήματος είναι αυτές των χρόνων και
// συμφωνούν μαζί τους, αλλιώς χεριές ÷ χρόνο (η «SR από χρόνους χεριών» φεύγει, το μήκος κύκλου στη θέση της). Στο script πριν
// από τα αγγλικά: η σημείωσή της και οι συναρτήσεις που άλλαξαν όπως στο νέο, με τα swimT_ / swimTF_ στα ελληνικά και χωρίς τον
// ΔΑΚ κάθε 50άρι (όπως το NEW εδώ).
const SR_NOTE_OLD = "'SR από χρόνους χεριών: από τα διαστήματα των πατημάτων · Λευκά: πληκτρολόγηση · Γαλάζια: αυτόματα από το Kinovea · Γκρι: τύποι.'";
const SR_NOTE_NEW = "'StrRate: από τους χρόνους των χεριών (60 ÷ χρόνος κύκλου, όπως η συχνότητα του βίντεο) όπου συμφωνούν με τις χεριές του τμήματος, αλλιώς χεριές ÷ χρόνο · Λευκά: πληκτρολόγηση · Γαλάζια: αυτόματα από το Kinovea · Γκρι: τύποι.'";
assert.ok(ORIGINAL.indexOf(SR_NOTE_OLD) > 0 && CURRENT.indexOf(SR_NOTE_OLD) < 0 && CURRENT.indexOf(SR_NOTE_NEW) > 0, 'the 2.11.0 note of the stroke rate');
const fnOf = (src, name) => {
  const a = src.indexOf('\nfunction ' + name + '(');
  assert.ok(a >= 0 && src.indexOf('\nfunction ' + name + '(', a + 1) < 0, 'one function ' + name);
  return src.slice(a + 1, src.indexOf('\n}\n', a) + 3);
};
const SR_SAME = { 'WIDE: yards ? 17 : 15': 'WIDE: yards ? 16 : 15',
  "cell(r, 9, '=IF(O' + tr + '=\"\",\"\",O' + tr + ')', SWIM_FMT.dps);": "cell(r, 9, '=IF(M' + tr + '=\"\",\"\",M' + tr + ')', SWIM_FMT.dps);" };
let ORIGINAL_NAMED = ORIGINAL.replace(UW_OLD, UW_NEW).replace(SR_NOTE_OLD, SR_NOTE_NEW);
['swimAnalyze_', 'swimBlocks50_', 'swimLayoutTiming_', 'swimInputPlan_', 'swimCleanFormulas_'].forEach(name => {
  ORIGINAL_NAMED = ORIGINAL_NAMED.split(fnOf(ORIGINAL_NAMED, name)).join(fnOf(CURRENT, name));
});
Object.keys(SR_SAME).forEach(k => {
  assert.ok(ORIGINAL_NAMED.split(k).length === 2 && CURRENT.indexOf(k) < 0 && CURRENT.indexOf(SR_SAME[k]) > 0, 'the 2.11.0 change ' + k);
  ORIGINAL_NAMED = ORIGINAL_NAMED.split(k).join(SR_SAME[k]);
});
ORIGINAL_NAMED += '\n' + ['swimCycleRate_', 'swimSrMatches_', 'swimSrFormula_'].map(n => fnOf(CURRENT, n)).join('\n') +
  'function swimT_(el) { return el; }\n' +
  'function swimTF_(el) { var a = arguments; return el.replace(/\\{(\\d+)\\}/g, function (all, n) { var i = Number(n) + 1; return i < a.length ? String(a[i]) : all; }); }\n' +
  'var SWIM_DAK50 = false;\n';

// Το mock των υπηρεσιών με άλλο script (το mock_gas διαβάζει το αρχείο όταν φορτώνεται).
function makeContextWith(src, opts) {
  const file = require.resolve('./mock_gas');
  delete require.cache[file];
  const read = fs.readFileSync;
  fs.readFileSync = function (p) { return /SwimRaceAnalysis\.gs$/.test(String(p)) ? src : read.apply(fs, arguments); };
  try {
    return require('./mock_gas').makeContext(opts);
  } finally {
    fs.readFileSync = read;
    delete require.cache[file];
  }
}

// ---------------------------------------------------------------------------------------------
// Καταγραφή: ό,τι γράφει το script σε κάθε φύλλο εκτός από τις τιμές (μορφοποίηση, ενώσεις κελιών, σημειώσεις)
// ---------------------------------------------------------------------------------------------
const LOGGED = ['merge', 'breakApart', 'setNote', 'setNumberFormat', 'setNumberFormats', 'setBackground', 'setBackgrounds', 'setFontWeight',
  'setFontWeights', 'setFontStyles', 'setFontColors', 'setFontSizes', 'setHorizontalAlignments', 'setWrap', 'setWraps', 'setVerticalAlignment',
  'setVerticalAlignments', 'setFontFamily', 'setBorder', 'clearContent', 'setDataValidation', 'clearDataValidations'];
function record(sh) {
  if (sh._log) return sh;
  sh._log = [];
  sh._notes = {};
  const getRange = sh.getRange;
  sh.getRange = function () {
    const rng = getRange.apply(sh, arguments);
    LOGGED.forEach(fn => {
      const f = rng[fn];
      if (typeof f !== 'function') return;
      rng[fn] = function () {
        const args = Array.from(arguments);
        sh._log.push([fn, rng._r, rng._c, rng._nr, rng._nc, JSON.stringify(args)]);
        if (fn === 'setNote') sh._notes[rng._r + ':' + rng._c] = String(args[0]);
        return f.apply(rng, arguments);
      };
    });
    return rng;
  };
  const getRangeList = sh.getRangeList;
  sh.getRangeList = function (list) {
    const rl = getRangeList.apply(sh, arguments);
    const set = rl.setNumberFormat;
    rl.setNumberFormat = f => { sh._log.push(['rangeList.setNumberFormat', JSON.stringify(list), f]); set(f); return rl; };
    return rl;
  };
  return sh;
}
function recordAll(G) {
  const wrap = ss => {
    if (ss._recorded) return ss;
    ss._recorded = true;
    ss.getSheets().forEach(record);
    const insert = ss.insertSheet;
    ss.insertSheet = function () { return record(insert.apply(ss, arguments)); };
    return ss;
  };
  wrap(G._ss);
  const create = G.SpreadsheetApp.create;
  G.SpreadsheetApp.create = function () { return wrap(create.apply(this, arguments)); };
}

const sorted = o => { const out = {}; Object.keys(o || {}).sort().forEach(k => { out[k] = o[k]; }); return out; };
function sheetState(sh) {
  const cells = {};
  Object.keys(sh._cells).sort().forEach(k => { cells[k] = sh._cells[k].v; });
  const box = r => [r._r, r._c, r._nr, r._nc];
  return {
    name: sh.getName(), cells, rowHeights: sorted(sh._rowHeights), colWidths: sorted(sh._colWidths), forced: sorted(sh._forced),
    notes: sorted(sh._notes), log: sh._log || [], rules: (sh._rules || []).map(r => [r.formula, r.bg, r.color]),
    charts: sh._charts.map(c => ({ type: c.type, opts: c.opts, ranges: c.ranges.map(box), pos: c.pos })),
    images: sh._images.map(im => ({ row: im.row, col: im.col, ox: im.ox, oy: im.oy, w: im.w, h: im.h, alt: im.alt || '', blob: im.blob._name }))
  };
}
const stateOf = G => Object.keys(G._all).sort().map(id => ({ id, sheets: G._all[id].getSheets().map(sheetState) }));

// Η πρώτη διαφορά δύο τιμών (διαδρομή και τιμές), αλλιώς null.
function firstDiff(a, b, where) {
  where = where || '$';
  if (a === b) return null;
  if (typeof a !== 'object' || typeof b !== 'object' || a === null || b === null || Array.isArray(a) !== Array.isArray(b))
    return where + ': ' + JSON.stringify(a) + ' ≠ ' + JSON.stringify(b);
  const keys = Array.from(new Set(Object.keys(a).concat(Object.keys(b))));
  for (const k of keys) {
    if (!(k in a) || !(k in b)) return where + '.' + k + ': ' + (k in a ? 'only in the first' : 'only in the second');
    const d = firstDiff(a[k], b[k], where + '.' + k);
    if (d) return d;
  }
  return null;
}

// ---------------------------------------------------------------------------------------------
// Οι αγώνες (φανταστικοί): ένας πλήρης με τεχνική, στιγμιότυπα, γωνίες, τελικό και συγκρίσεις, μία προπόνηση, γιάρδες,
// μικτή με επίπεδα, σκυταλοδρομία μικτή, και ένας με λάθος χρόνο (προειδοποίηση), ακύρωση και σκέλος σκυταλοδρομίας
// ---------------------------------------------------------------------------------------------
const ATHLETES = [['unique_id', 'ΟΝΟΜΑΤΕΠΩΝΥΜΟ', 'ΦΥΛΟ', 'ΕΤΟΣ ΓΕΝΝΗΣΗΣ', 'ΚΑΤΗΓΟΡΙΑ', 'ΣΩΜΑΤΕΙΟ'],
  [910001, 'TESTER Anna', 'Γυναικών', 2008, 'Γυναικών', 'Test Swim Club']];
// Ένα όνομα γωνίας που έγραψε ο αναλυτής στα ελληνικά: μένει όπως είναι και στα αγγλικά.
const CUSTOM_ANGLE = 'Κλίση λεκάνης';
const jpeg = Buffer.from([0xff, 0xd8, 0xff, 0xe0, 1, 2, 3, 4, 0xff, 0xd9]).toString('base64');
const png = Buffer.from([137, 80, 78, 71, 13, 10, 26, 10, 9, 9, 9]).toString('base64');
const who = { athlete: 'TESTER Anna', athleteId: '910001', gender: 'Γυναικών', club: 'Test Swim Club', category: 'Γυναικών', birthYear: '2008',
  competition: 'Test Open 2026', location: 'Testville' };
const cps100 = [15, 25, 35, 45, 50, 65, 75, 85, 95, 100];
const t100 = [6.72, 12.48, 18.36, 24.26, 27.42, 36.1, 42.12, 48.36, 54.76, 57.94];
const strokes100 = scale => {
  const out = [];
  for (let t = 5.9; t < 27; t += 1.12) out.push(+(t * scale).toFixed(2));
  for (let t = 33.2; t < 57.6; t += 1.18) out.push(+(t * scale).toFixed(2));
  return out;
};
const finalsOf = () => ({
  label: 'OG2024 · 100μ. fly Γυναικών', source: 'Test finals',
  swimmers: [['Alpha ONE', 0.95], ['Beta TWO', 0.955], ['Gamma THREE', 0.96], ['Delta FOUR', 0.965]].map(([name, sc], i) => ({
    rank: i + 1, name, times: [15, 50, 100].map(d => ({ d, t: +({ 15: 6.72, 50: 27.42, 100: 57.94 }[d] * sc).toFixed(2) })) })),
  // Όπως τα στέλνει το Kinovea (SwimFinalsCompare): ομάδες, ετικέτες και μονάδες στα ελληνικά.
  metrics: [
    { group: 'Εκκίνηση', label: 'Αποκόλληση από τον βατήρα', athlete: '0.70', rank: '5/5', winner: '0.63', mean: '0.66', best: '0.61', diff: '+0.07' },
    { group: 'Εκκίνηση', label: '15μ. εκκίνηση', athlete: '6.72', rank: '5/5', winner: '6.38', mean: '6.42', best: '6.38', diff: '+0.34' },
    { group: 'Εκκίνηση', label: 'Έξοδος: απόσταση', athlete: '11.5 μ.', rank: '', winner: '12.0 μ.', mean: '11.8 μ.', best: '13.1 μ.', diff: '−0.5' },
    { group: 'Περάσματα', label: '50μ.', athlete: '27.42', rank: '5/5', winner: '26.05', mean: '26.32', best: '26.05', diff: '+1.37' },
    { group: 'Στροφή 50μ.', label: '5μ. πριν + 15μ. μετά', athlete: '11.71', rank: '4/5', winner: '11.30', mean: '11.52', best: '11.30', diff: '+0.41' },
    { group: 'Στροφή 50μ.', label: 'Έξοδος: χρόνος από τον τοίχο', athlete: '3.58', rank: '', winner: '3.90', mean: '3.71', best: '3.20', diff: '−0.32' },
    { group: 'Τερματισμός', label: 'Τελευταία 5μ.', athlete: '3.18', rank: '4/5', winner: '3.02', mean: '3.06', best: '2.86', diff: '+0.16' },
    { group: 'Χεριές', label: '0–50μ.', athlete: '19.0 χεριές', rank: '', winner: '18.0 χεριές', mean: '18.5 χεριές', best: '17.0 χεριές', diff: '+1.0' },
    { group: 'Συχνότητα', label: '50–100μ.', athlete: '52.5 κύκλοι/λ.', rank: '', winner: '54.1 κύκλοι/λ.', mean: '53.0 κύκλοι/λ.', best: '55.0 κύκλοι/λ.', diff: '−1.6' },
    { group: 'Μήκος κύκλου', label: '50–100μ.', athlete: '1.85 μ./κύκλο', rank: '', winner: '1.95 μ./κύκλο', mean: '1.90 μ./κύκλο', best: '2.01 μ./κύκλο', diff: '−0.10' }
  ]
});

function races(files, reference) {
  const pb = Object.assign({ id: 'lang-pb', style: 'fly', distance: 100, pool: 50, round: 'Ημιτελικός', date: '1.6.2026', lane: 3,
    checkpoints: cps100.map((d, i) => ({ d, t: +(t100[i] * 1.02).toFixed(2) })), strokes: strokes100(1.02), reference }, who);
  const full = Object.assign({ id: 'lang-full', style: 'fly', distance: 100, pool: 50, round: 'Τελικός', date: '16.10.2026', lane: 4,
    checkpoints: cps100.map((d, i) => ({ d, t: t100[i] })), strokes: strokes100(1),
    kicks: [1.5, 2.0, 2.5, 3.0, 28.5, 29.0], breakouts: [5.2, 31.0], feetOff: [0.7, 28.3], entries: [1.02],
    manual: { reaction: 0.66, entryDistHands: 3.3, entryDistHips: 3.0, underwaterDist: 11.5, breakoutDist: 11.8,
      turns: { 50: { underwaterDist: 8.4, turnTime: 0.85 } }, finishLastStrokeDist: 0.4 },
    reference, compare: { pb: 'auto' }, finals: finalsOf(),
    finalFrame: { fileId: files.final, t: 57.94 },
    graphics: [{ id: 'summary', fileId: files.summary }, { id: 'versus', fileId: files.versus }],
    tech: {
      phases: [
        { id: 'start', kind: 'start', label: 'Εκκίνηση', values: { reaction: 0.21, handsOff: 0.39, backFoot: 0.55, blockTime: 0.7, blockPhase: 0.49, flight: 0.32,
          entryTime: 1.02, entryDist: 3.3, t5: 1.7, t10: 3.8, t15: 6.72, v0_5: 2.9, v5_10: 2.4, v10_15: 1.7, underwater: 4.18, kicks: 4, kickRate: 2.3,
          kickDist: 1.6, breakoutTime: 5.2, breakoutDist: 11.8, uwVel: 2.2, firstStroke: 5.9 } },
        { id: 'turn50', kind: 'turn', wall: 50, label: 'Στροφή 50μ.', values: { in5: 3.16, lastStrokeGap: 0.6, lastStrokeDist: 1.2, rotation: 0.4,
          contact: 0.3, turnTime: 0.85, out5: 1.5, out10: 3.6, out15: 6.3, inOut5: 4.66, inOut10: 6.76, inOut15: 9.46, kicks: 2, breakoutDist: 8.4 } }
      ],
      angles: [
        { id: 'a1', name: 'Γόνατο', t: 0.05, deg: 128, phase: 'start' },
        { id: 'a2', name: 'Γωνία εισόδου στο νερό', t: 1.02, deg: 38, phase: 'start' },
        { id: 'a3', name: CUSTOM_ANGLE, t: -0.1, deg: 22, phase: 'turn50' },
        { id: 'a4', name: 'Ώμος', t: 40.0, deg: 95, phase: '' }
      ],
      snapshots: [
        { id: 's1', t: 0, phase: 'start', stage: 'Θέση ετοιμότητας', event: 'Σήμα εκκίνησης', comment: 'Weight too far back.', fileId: files.s1,
          eliteFileId: files.e1, eliteLabel: 'Elite ALPHA – ready', eliteComment: 'Higher hips.' },
        { id: 's2', t: -0.6, phase: 'turn50', stage: 'Προσέγγιση', event: 'Τελευταία χεριά', comment: '' },
        { id: 's3', t: 0, phase: 'turn50', stage: 'Επαφή στον τοίχο', event: 'Επαφή στον τοίχο', comment: 'Slow rotation.' }
      ],
      values: { notes: 'Goal: a faster release from the block.' }
    } }, who);
  const phases = [];
  for (let i = 1; i <= 2; i++) phases.push({ id: 'st' + i, kind: 'start', attempt: i, label: 'Εκκίνηση ' + i, values: { reaction: 0.2 + i / 100, blockTime: 0.7,
    flight: 0.3, t5: 1.6, t10: 3.6 + i / 50, t15: 6.0 + i / 20, kicks: 6 + i, breakoutDist: 11 + i / 2, kickRate: 2.4, uwVel: 2.1 } });
  for (let i = 1; i <= 3; i++) phases.push({ id: 'tu' + i, kind: 'turn', attempt: i, label: 'Στροφή ' + i, values: { in5: 2.6, lastStrokeGap: 0.5,
    turnTime: 0.8 + i / 100, contact: 0.25, rotation: 0.45, out5: 1.3, out10: 3.5, out15: 6.3 + i / 50, kicks: 5, breakoutDist: 9 } });
  const training = Object.assign({ id: 'lang-training', kind: 'training', style: 'fs', pool: 25, date: '10.10.2026', checkpoints: [],
    tech: { phases,
      angles: [{ id: 'g1', name: 'Γόνατο', t: 0.1, deg: 131.5, phase: 'st1' }, { id: 'g2', name: 'Γόνατο', t: 30.1, deg: 128, phase: 'st2' },
        { id: 'g3', name: CUSTOM_ANGLE, t: 0.2, deg: 18, phase: 'tu1' }, { id: 'g4', name: 'Κορμός', t: 50, deg: 12, phase: '' },
        { id: 'g5', name: '', t: 51, deg: 40, phase: '' }],
      snapshots: [{ id: 'n1', t: 0.12, phase: 'st1', stage: 'Θέση ετοιμότητας', comment: 'Weight too far back.', fileId: files.s1,
        eliteFileId: files.e1, eliteLabel: 'Elite BETA – ready', eliteComment: 'Higher hips.' },
      { id: 'n2', t: -0.62, phase: 'tu2', stage: 'Προσέγγιση', event: 'Τελευταία χεριά', comment: '' },
      { id: 'n3', t: 0, phase: 'tu2', stage: 'Επαφή στον τοίχο', event: 'Επαφή στον τοίχο', comment: '' },
      { id: 'n4', t: 1.1, phase: 'st2', stage: 'Πτήση', event: '', comment: 'Flat entry.' }],
      values: { notes: 'Goal: a faster release.' } } }, who);
  const cps25 = []; for (let w = 0; w < 100; w += 25) [15, 20, 25].forEach(p => cps25.push(w + p));
  const yt = [5.9, 8.6, 11.3, 18.2, 20.9, 23.6, 30.5, 33.2, 35.9, 42.7, 45.4, 48.0];
  const yStrokes = []; for (let L = 0; L < 4; L++) { const t0 = [4.2, 13.9, 26.2, 38.5][L]; for (let i = 0; i < 9; i++) yStrokes.push(+(t0 + i * 0.75).toFixed(2)); }
  const yards = Object.assign({ id: 'lang-yards', style: 'fs', distance: 100, pool: 25, poolUnit: 'yd', round: 'Προκριματικός', date: '1.3.2026',
    checkpoints: cps25.map((d, i) => ({ d, t: yt[i] })), strokes: yStrokes }, who);
  const imCps = []; for (let w = 0; w < 200; w += 50) [15, 25, 35, 45, 50].forEach(p => imCps.push(w + p));
  const im = Object.assign({ id: 'lang-im', style: 'im', distance: 200, pool: 50, round: 'Τελικός', date: '2.7.2026',
    checkpoints: imCps.map((d, i) => ({ d, t: +(d * 0.65 + (i % 5) * 0.01).toFixed(2) })) }, who);
  const relayCps = []; for (let w = 0; w < 400; w += 50) [15, 50].forEach(p => relayCps.push(w + p));
  const relay = Object.assign({ id: 'lang-relay', style: 'im', relay: true, distance: 400, distanceLabel: '4x100', pool: 50, round: 'Τελικός', date: '3.7.2026',
    checkpoints: relayCps.map(d => ({ d, t: +(d * 0.62).toFixed(2) })) }, who);
  const warn = Object.assign({ id: 'lang-warn', style: 'fs', distance: 50, pool: 50, round: 'Τελικός', date: '4.7.2026', dq: true, relayLeg: '2',
    checkpoints: [15, 25, 35, 45, 50].map((d, i) => ({ d, t: [6.5, 6.4, 16.0, 21.0, 23.5][i] })) }, who);
  return { pb, full, training, yards, im, relay, warn };
}

// Ένα Google Sheet με αθλητές, αναφορές (τελικός της ίδιας πηγής) και επίπεδα· οι εικόνες των αγώνων στο Drive.
function prepare(G) {
  recordAll(G);
  G.swimShowConnectionInfo = () => {};
  G.swimSetup();
  const call = body => JSON.parse(G.swimDoPost({ postData: { contents: JSON.stringify(Object.assign({ token: 'tok', app: 'kinovea-swim' }, body)) } }).text);
  const reports = G._all[Object.keys(G._all).find(k => k !== 'DATA')];
  const refRows = [];
  [['Ref ALPHA', 0.94], ['Ref BETA', 0.95], ['Ref GAMMA', 0.955]].forEach(([name, sc]) => {
    cps100.forEach((d, i) => refRows.push([i === 0 ? name : '', i === 0 ? 'Γυναικών' : '', i === 0 ? 'fly' : '', i === 0 ? '100' : '', i === 0 ? '50' : '',
      i === 0 ? 'Test Final 2025' : '', d, String(+(t100[i] * sc).toFixed(2)), '', [4, 4, 4.5, 4.5, 2.5, 4, 4.5, 4.5, 4.5, 2.5][i]]));
  });
  reports.getSheetByName('Elite αναφορές').getRange(2, 1, refRows.length, 10).setValues(refRows);
  reports.getSheetByName('Επίπεδα').getRange(2, 1, 5, 13).setValues([
    ['Εκκίνηση 0-15μ.', 'Γυναικών', 'fly', '100', '50', '', 6.0, 6.2, 6.3, 6.4, 6.6, 6.8, 'Test data'],
    ['Στροφή τοίχος-15μ.', 'Γυναικών', 'fly', '100', '50', '', 6.6, 6.8, 6.9, 7.0, 7.2, 7.4, 'Test data'],
    ['Στροφή', 'Γυναικών', 'im', '200', '50', 'Πεταλούδα → Ύπτιο', 6.1, 6.3, 6.4, 6.6, '', '', ''],
    ['Στροφή', 'Γυναικών', 'im', '200', '50', 'Ύπτιο → Πρόσθιο', 6.9, 7.1, 7.2, 7.4, '', '', ''],
    ['Στροφή', 'Γυναικών', 'im', '200', '50', 'Πρόσθιο → Ελεύθερο', 6.5, 6.7, 6.8, 7.0, '', '', '']]);
  const img = (raceId, id) => call({ action: 'image', raceId, snapshotId: id, mime: 'image/jpeg', data: jpeg }).fileId;
  const files = { final: img('lang-full', 'final'), summary: img('lang-full', 'summary'), versus: img('lang-full', 'versus'),
    s1: img('lang-full', 's1'), e1: img('lang-full', 'e1') };
  const refs = call({ action: 'references' });
  assert.ok(refs.ok && refs.references.length === 3, JSON.stringify(refs).slice(0, 200));
  return { call, reports, files, reference: refs.references[0] };
}

// Όλα τα αιτήματα των αγώνων (με lang, ή χωρίς όταν lang === undefined): οι απαντήσεις τους.
function postAll(env, R, lang) {
  const out = [];
  const send = body => { const r = env.call(lang === undefined ? body : Object.assign({ lang }, body)); out.push(r); return r; };
  send({ action: 'update', race: R.pb });
  const first = send({ action: 'update', race: R.full, drawCharts: true });
  const need = send({ action: 'pdf', race: R.full, drawCharts: true });
  assert.ok(need.ok && need.needCharts && need.charts.length > 10, JSON.stringify(need).slice(0, 300));
  // Τα γραφήματα από το «Kinovea»: ανεβαίνουν και το PDF γίνεται με αυτά.
  const up = send({ action: 'images', raceId: R.full.id, images: need.charts.map(c => ({ id: c.key, mime: 'image/png', data: png })) });
  const hash = {}; need.charts.forEach(c => { hash[c.key] = c.hash; });
  const withImages = Object.assign({}, R.full, { chartImages: up.files.filter(f => f.fileId).map(f => ({ id: f.id, fileId: f.fileId, hash: hash[f.id] })) });
  const pdf = send({ action: 'pdf', race: withImages, drawCharts: true });
  assert.ok(pdf.ok && pdf.pdf && pdf.pdf.url && !pdf.needCharts, JSON.stringify(pdf).slice(0, 300));
  ['training', 'yards', 'im', 'relay', 'warn'].forEach(k => send({ action: 'update', race: R[k], drawCharts: true }));
  send({ action: 'analyses', query: { athleteId: '910001', style: 'fly', distance: 100, pool: 50, poolUnit: 'm', raceId: 'x' } });
  send({ action: 'addAlias' });
  send({ action: 'nothing-like-this' });
  return { responses: out, first };
}

// ---------------------------------------------------------------------------------------------
// (a) Ελληνικά: lang 'el' στο νέο script = το script πριν από τα αγγλικά, κελί προς κελί (εκτός από την έκδοση)
// ---------------------------------------------------------------------------------------------
const OLD = makeContextWith(ORIGINAL_NAMED, { athletes: ATHLETES });
// 2.8.0: χωρίς copyTo η πρώτη σελίδα του PDF («Με μια ματιά») δεν γίνεται και το PDF είναι όπως πριν· ελέγχεται στο test_quick.
const NEW = makeContext({ athletes: ATHLETES, copyTo: false });
// 2.9.5: ο ΔΑΚ κάθε 50άρι (στήλη L των χρόνων, πλακίδιο) και τα γραφήματα του PDF (χωρίς τιμές πάνω στις σειρές, χωρίς τα
// άδεια) κλειστά για τη σύγκριση· ανοίγουν στο (b) και ελέγχονται στα test_v2, test_charts και test_graphics.
NEW.SWIM_DAK50 = false;
NEW.SWIM_PDF_CHARTS = false;
const envOld = prepare(OLD), envNew = prepare(NEW);
assert.deepStrictEqual(envOld.files, envNew.files, 'same Drive ids in both');
const Rold = races(envOld.files, envOld.reference), Rnew = races(envNew.files, envNew.reference);
const outOld = postAll(envOld, Rold, undefined);
const outNew = postAll(envNew, Rnew, 'el');
const vOld = versionOf(ORIGINAL), vNew = versionOf(CURRENT);
assert.strictEqual(vNew, '2.11.0');
const norm = (x, v) => JSON.parse(JSON.stringify(x).split(v).join('<version>'));
// 2.8.0: οι αναλύσεις του αθλητή και με τους χρόνους τους (για σύγκριση στο βίντεο)· το PDF λέει αν έγινε η πρώτη σελίδα (εδώ
// όχι: χωρίς copyTo).
const withoutTimes = list => list.map(r => {
  // 2.10.0: το αποτέλεσμα της καταχώρισης στο «Αρχεία Αθλητών/τριων» (εδώ χωρίς το φύλλο ή χωρίς αθλητή του μητρώου).
  if (r && 'archive' in r) {
    assert.ok(r.archive && typeof r.archive.ok === 'boolean' && r.archive.ok === false, 'archive result: ' + JSON.stringify(r.archive));
    r = Object.assign({}, r);
    delete r.archive;
  }
  if (r && r.pdf && 'quick' in r.pdf) {
    assert.strictEqual(r.pdf.quick, false, 'no first page without copyTo');
    r = Object.assign({}, r, { pdf: Object.assign({}, r.pdf) });
    delete r.pdf.quick;
  }
  // 2.11.0: τα σχόλια στο τέλος της πλήρους ανάλυσης (εδώ όχι: χωρίς copyTo).
  if (r && r.pdf && 'notes' in r.pdf) {
    assert.strictEqual(r.pdf.notes, false, 'no comments without copyTo');
    r = Object.assign({}, r, { pdf: Object.assign({}, r.pdf) });
    delete r.pdf.notes;
  }
  return r && Array.isArray(r.analyses) ? Object.assign({}, r, { analyses: r.analyses.map(a => {
    assert.ok(Array.isArray(a.times) && a.times.length > 0, 'times of an analysis');
    const c = Object.assign({}, a); delete c.times; return c;
  }) }) : r;
});
let diff = firstDiff(norm(outOld.responses, vOld), norm(withoutTimes(outNew.responses), vNew));
assert.strictEqual(diff, null, 'responses differ in Greek: ' + diff);
diff = firstDiff(norm(stateOf(OLD), vOld), norm(stateOf(NEW), vNew));
assert.strictEqual(diff, null, 'sheets differ in Greek: ' + diff);
// Η σύγκριση δεν είναι κενή: κάθε report έχει ενωμένα κελιά και μορφοποίηση, τα φύλλα δεδομένων σημειώσεις.
const reportsOld = envOld.reports.getSheets();
assert.ok(reportsOld.filter(s => s._log.filter(x => x[0] === 'merge').length > 30 && s._log.some(x => x[0] === 'setBackgrounds')).length >= 7, 'report formatting recorded');
assert.ok(reportsOld.some(s => Object.keys(s._notes).length >= 10), 'notes recorded');
assert.ok(reportsOld.some(s => s._charts.length > 10) && reportsOld.some(s => s._images.length > 10), 'charts and images recorded');
// Οι εξαγωγές PDF (της μέτρησης της σελίδας: προσωρινό φύλλο με την ώρα στο όνομα).
const exportsOf = (G, v) => norm(G._exports, v).map(e => Object.assign(e, { name: e.name.replace(/^_swim_pdf_pages_\d+$/, '_swim_pdf_pages_<time>') }));
assert.deepStrictEqual(exportsOf(OLD, vOld), exportsOf(NEW, vNew), 'same PDF exports');
// Και τα layouts (καθαρές συναρτήσεις), με όλη τη μορφοποίηση κάθε κελιού.
const layoutOf = (G, env, raw) => {
  const race = G.swimNormalizeRace_(raw);
  const x = race.kind === 'training' ? {} : G.swimComparisons_(race);
  x.pageHeight = w => G.swimPdfPageHeight_(env.reports, w);
  return G.swimBuildLayout_(race, G.swimAnalyze_(race), x);
};
Object.keys(Rnew).forEach(k => {
  const d = firstDiff(JSON.parse(JSON.stringify(layoutOf(OLD, envOld, Rold[k]))), JSON.parse(JSON.stringify(layoutOf(NEW, envNew, Rnew[k]))));
  assert.strictEqual(d, null, 'layout of ' + k + ' differs in Greek: ' + d);
});
assert.ok(outNew.responses.some(r => (r.warnings || []).some(w => /^Ο χρόνος στα 25μ\. δεν είναι μεγαλύτερος/.test(w))), 'Greek warning');
assert.strictEqual(NEW.SWIM_LANG_, 'el');
console.log('test_lang (a): Greek output identical to the script ' + vOld + ' (' + Object.keys(Rnew).length + ' races, ' +
  outNew.responses.length + ' requests, ' + stateOf(NEW).reduce((n, ss) => n + ss.sheets.length, 0) + ' sheets)');

// ---------------------------------------------------------------------------------------------
// (b) Αγγλικά: οι ίδιοι αγώνες με lang 'en' ξαναφτιάχνουν τα report· κανένα ελληνικό γράμμα σε ό,τι φαίνεται, εκτός από
// τις τιμές που έγραψε ο αναλυτής στα ελληνικά (η γωνία CUSTOM_ANGLE) και το σύμβολο Δ (διαφορά, όπως στο Kinovea)
// ---------------------------------------------------------------------------------------------
const GREEK = /[Ͱ-Ͽἀ-῿]/;
NEW.SWIM_DAK50 = true;
NEW.SWIM_PDF_CHARTS = true;
const outEn = postAll(envNew, Rnew, 'en');
assert.ok(outEn.first.ok && outEn.first.rebuilt, 'another language rebuilds the report');
assert.strictEqual(NEW.SWIM_LANG_, 'el', 'Greek again after the request');
const DATA_SHEETS = ['Αγώνες', 'Elite αναφορές', 'Επίπεδα', 'Elite καρέ'];
const reportSheets = envNew.reports.getSheets().filter(s => DATA_SHEETS.indexOf(s.getName()) < 0);
assert.strictEqual(reportSheets.length, Object.keys(Rnew).length, reportSheets.map(s => s.getName()).join(' | '));
const shown = [];
const seen = (text, where) => { if (typeof text === 'string' && text) shown.push({ text, where }); };
reportSheets.forEach(sh => {
  Object.keys(sh._cells).forEach(k => seen(sh._cells[k].v, sh.getName() + '!' + k));
  Object.keys(sh._notes).forEach(k => seen(sh._notes[k], sh.getName() + '!note ' + k));
  sh._charts.forEach(c => {
    const o = c.opts, at = sh.getName() + '!chart ' + o.title;
    seen(o.title, at); seen(o.hAxis && o.hAxis.title, at);
    Object.keys(o.vAxes || {}).forEach(i => seen(o.vAxes[i].title, at));
    Object.keys(o.series || {}).forEach(i => seen(o.series[i].labelInLegend, at));
  });
});
let chartSpecs = 0;
outEn.responses.forEach(r => {
  (r.warnings || []).forEach(w => seen(w, 'warning'));
  (r.charts || []).forEach(c => {
    chartSpecs++;
    [c.title, c.vTitle, c.v2Title, c.hTitle].forEach(t => seen(t, 'spec ' + c.title));
    c.series.forEach(s => seen(s.name, 'spec ' + c.title));
    c.categories.forEach(t => seen(t, 'spec ' + c.title));
  });
});
assert.ok(chartSpecs > 30, 'chart specs checked: ' + chartSpecs);
const leftover = shown.filter(x => GREEK.test(x.text.split(CUSTOM_ANGLE).join('').replace(/(^|[^Ͱ-Ͽ])Δ(?![Ͱ-Ͽ])/g, '$1')));
assert.strictEqual(leftover.length, 0, 'Greek left in the English report:\n' + leftover.slice(0, 25).map(x => x.where + ': ' + x.text).join('\n'));
// Λίγα δείγματα: τίτλοι, τιμές του Kinovea, γωνία του αναλυτή, μονάδες, ετικέτα του τελικού.
const texts = shown.map(x => x.text);
const has = (t, why) => assert.ok(texts.indexOf(t) >= 0, (why || 'missing') + ': ' + t);
const like = (re, why) => assert.ok(texts.some(t => re.test(t)), (why || 'missing') + ': ' + re);
has('Times & Strokes (Timing Data)');
has('SPI'); has('Speed per segment (m/s)'); has('Stroke rate (cycles/min)'); has('Race'); has('Swimmer');
// 2.9.5: ο ΔΑΚ κάθε 50άρι στα αγγλικά: η στήλη L των χρόνων, το πλακίδιο (μέσος όρος, από κάτω κάθε 50άρι).
has('SPI of the 50'); has('SPI (mean)');
like(/^=IF\(COUNT\(L\d+,L\d+\)=0,"Swimming Performance Index \(average of the 50s\)",IF\(ISNUMBER\(L\d+\),"1st "&TEXT\(L\d+,"0\.00"\),"1st –"\)&" · "&IF\(ISNUMBER\(L\d+\),"2nd "&TEXT/, 'SPI of each 50 under the tile');
like(/^100m Butterfly · Pool 50m · Final$/, 'event line');
like(/^Test Swim Club · Reg\. no\. 910001 · Women · 2008$/, 'club line');
like(/^TESTER Anna {2}\(Lane 4\)$/, 'athlete and lane');
like(/^Updated .* · version 2\.11\.0$/, 'version line');
has('Turn 50m'); has('Start 2'); has('Turn 3'); has('Angle: Knee (°)'); has('Water entry angle (°)'); has('Angle: ' + CUSTOM_ANGLE + ' (°)');
has('Shoulder'); has('Trunk'); has('Angle');
like(/^Start · Start signal · Ready position · t = 0\.00 s$/, 'snapshot title with the event before the stage');
like(/^Turn 50m · Last stroke · Approach · t = -0\.60 s$/, 'snapshot of the turn');
like(/^Turn 2 · Wall contact · t = 0\.00 s$/, 'event = stage once');
like(/^The race among the finalists: OG2024 · 100m fly Women \(4\)$/, 'finals section');
['Block release', '15m start', 'Breakout: distance', '11.5 m', '50m', 'Turn 50m', '5m before + 15m after', 'Last 5m', '0–50m', '19.0 strokes',
  '52.5 cycles/min', '1.85 m/cycle', 'Cycle length', 'Splits'].forEach(t => has(t, 'finals value'));
like(/^Pacing per 50m: swimmer – OG2024 · 100m fly Women \(4 finalists\)$/, 'finalists per 50');
like(/^PB: Semi-final · Test Open 2026 · 1\.6\.2026 · /, 'PB legend with the round in English');
like(/below Junior medal/, 'level formula in English');
like(/^Turn 50m: Butterfly → Backstroke$/, 'IM turn levels');
has('Leg 1: Backstroke'); has('relay exchange');
like(/^100yd Freestyle · Pool 25yd · Heats$/, 'yards event line');
like(/^50m Freestyle \(relay leg\) · Pool 50m · Final · Disqualified \(DQ\)$/, 'relay leg and DQ');
like(/^TECHNICAL ANALYSIS · STARTS & TURNS$/); like(/^Technical analysis \(training\) · Freestyle · Pool 25m$/);
assert.ok(outEn.responses.some(r => (r.warnings || []).indexOf('The time at 25m is not greater than the previous one.') >= 0), 'English warning');
// Τα μηνύματα προς το Kinovea στα αγγλικά· το «Άγνωστη ενέργεια» ελληνικά (το αναγνωρίζει το Kinovea).
assert.strictEqual(outEn.responses[outEn.responses.length - 2].error, 'The code of the swimmer and the name are needed.');
assert.ok(/^Άγνωστη ενέργεια: /.test(outEn.responses[outEn.responses.length - 1].error));
assert.ok(/^Wrong token\./.test(envNew.call({ token: 'bad', lang: 'en', action: 'ping' }).error));
assert.ok(/^Λάθος token\./.test(envNew.call({ token: 'bad', action: 'ping' }).error));
// Τα φύλλα δεδομένων μένουν ελληνικά: η γραμμή του αγώνα στο «Αναλύσεις Αγώνων» (πισίνα, αγώνισμα) και το ευρετήριο.
const summary = NEW._ss.getSheetByName('Αναλύσεις Αγώνων');
const sumRows = summary.getRange(2, 1, summary.getLastRow() - 1, 23).getValues();
const fullRow = sumRows.find(r => r[21] === 'lang-full');
assert.ok(fullRow && fullRow[10] === '50μ.' && fullRow[7] === 'Τελικός', 'summary row stays Greek: ' + JSON.stringify(fullRow));
const stored = NEW.swimStoredRace_('lang-full');
assert.strictEqual(stored.race.lang, 'en', 'the language is kept with the race');
assert.ok(/"lang":"en"/.test(NEW.swimIndexSheet_(envNew.reports).getRange(stored.row, NEW.SWIM_IDX.layout + 1).getValue()), 'language in the signature');
console.log('test_lang (b): English report without Greek (' + shown.length + ' texts, ' + chartSpecs + ' chart specs)');

// ---------------------------------------------------------------------------------------------
// (c) swimL_: όπως το SwimLang.Label του Kinovea· στα ελληνικά οι τιμές όπως είναι
// ---------------------------------------------------------------------------------------------
const L = v => NEW.swimWithLang_('en', () => NEW.swimL_(v));
assert.strictEqual(L('Στροφή 50μ.'), 'Turn 50m');
assert.strictEqual(L('Εκκίνηση 2'), 'Start 2');
assert.strictEqual(L('25–50μ.'), '25–50m');
assert.strictEqual(L('Γόνατο'), 'Knee');
assert.strictEqual(L('Στροφή 25yd'), 'Turn 25yd');
assert.strictEqual(L('Τελικός'), 'Final');
assert.strictEqual(L('Γυναικών'), 'Women');
assert.strictEqual(L('OG2024 · 100μ. bk Ανδρών'), 'OG2024 · 100m bk Men');
assert.strictEqual(L('52.5 κύκλοι/λ.'), '52.5 cycles/min');
assert.strictEqual(L('1.85 μ.'), '1.85 m');
assert.strictEqual(L('2.05 μ./κύκλο'), '2.05 m/cycle');
assert.strictEqual(L('1:02.35'), '1:02.35');
assert.strictEqual(L('Κάτι που έγραψε ο αναλυτής'), 'Κάτι που έγραψε ο αναλυτής', 'unknown Greek value unchanged');
assert.strictEqual(L('Γόνατο αριστερό 2'), 'Γόνατο αριστερό 2', 'unknown words around a number: unchanged');
assert.strictEqual(L(CUSTOM_ANGLE), CUSTOM_ANGLE);
assert.strictEqual(L('Final'), 'Final');
assert.strictEqual(L(''), '');
assert.strictEqual(L(null), null);
assert.strictEqual(L(50), 50);
['Στροφή 50μ.', 'Εκκίνηση 2', '25–50μ.', 'Γόνατο', 'OG2024 · 100μ. bk Ανδρών', '52.5 κύκλοι/λ.'].forEach(v => {
  assert.strictEqual(NEW.swimL_(v), v, 'Greek: ' + v);
  assert.strictEqual(NEW.swimWithLang_('el', () => NEW.swimL_(v)), v, 'Greek: ' + v);
});
// swimT_ / swimTF_: τα ελληνικά όπως είναι, τα αγγλικά με τα ίδια κενά.
assert.strictEqual(NEW.swimT_('Στροφές'), 'Στροφές');
assert.strictEqual(NEW.swimWithLang_('en', () => NEW.swimT_('Στροφές')), 'Turns');
assert.strictEqual(NEW.swimWithLang_('en', () => NEW.swimT_('Κείμενο χωρίς αγγλικά')), 'Κείμενο χωρίς αγγλικά');
assert.strictEqual(NEW.swimTF_('Χρόνος {0} (s)', '50μ.'), 'Χρόνος 50μ. (s)');
assert.strictEqual(NEW.swimWithLang_('en', () => NEW.swimTF_('Χρόνος {0} (s)', '50m')), '50m time (s)');
assert.strictEqual(NEW.swimWithLang_('en', () => NEW.swimUnitLabel_({ poolUnit: 'm' })) + NEW.swimUnitLabel_({ poolUnit: 'yd' }), 'myd');
assert.strictEqual(NEW.swimUnitLabel_({ poolUnit: 'm' }), 'μ.');
// Το λεξικό: τα ίδια κενά {0}, {1}… και τα ίδια κενά στις άκρες· ελληνικά μόνο σε ονόματα του φύλλου σε εισαγωγικά και το Δ.
const EN = NEW.SWIM_EN_;
const tokens = s => (s.match(/\{\d+\}/g) || []).sort().join(',');
Object.keys(EN).forEach(el => {
  const en = EN[el];
  assert.strictEqual(tokens(en), tokens(el), 'placeholders of ' + el);
  assert.strictEqual(/^\s/.test(en), /^\s/.test(el), 'leading space of ' + el);
  assert.strictEqual(/\s$/.test(en), /\s$/.test(el), 'trailing space of ' + el);
  assert.ok(!GREEK.test(en.replace(/"[^"]*"/g, '').replace(/(^|[^Ͱ-Ͽ])Δ(?![Ͱ-Ͽ])/g, '$1')), 'Greek in the English of ' + el + ': ' + en);
});
console.log('test_lang (c): swimL_ / swimT_ ok, ' + Object.keys(EN).length + ' texts in SWIM_EN_');

// ---------------------------------------------------------------------------------------------
// (d) Η γλώσσα μόνο για το αίτημα: ένα αίτημα με 'en' και μετά ένα χωρίς lang (παλιό Kinovea) → ελληνικά
// ---------------------------------------------------------------------------------------------
const cellsOf = sh => Object.keys(sh._cells).map(k => sh._cells[k].v);
const sheetOf = r => envNew.reports.getSheets().find(s => s.getSheetId() === r.sheetId);
let r = envNew.call({ action: 'update', lang: 'en', race: Rnew.warn });
assert.ok(r.ok && r.warnings[0] === 'The time at 25m is not greater than the previous one.', JSON.stringify(r.warnings));
assert.ok(cellsOf(sheetOf(r)).indexOf('Times & Strokes (Timing Data)') >= 0);
r = envNew.call({ action: 'update', race: Rnew.warn });
assert.ok(r.ok && r.rebuilt, 'back to Greek: rebuilt');
assert.ok(/^Ο χρόνος στα 25μ\. δεν είναι μεγαλύτερος από τον προηγούμενο\.$/.test(r.warnings[0]), r.warnings[0]);
assert.ok(cellsOf(sheetOf(r)).indexOf('Χρόνοι & Χεριές (Timing Data)') >= 0 && cellsOf(sheetOf(r)).indexOf('Times & Strokes (Timing Data)') < 0, 'Greek report');
assert.strictEqual(NEW.swimStoredRace_('lang-warn').race.lang, undefined, 'Greek: no language kept');
assert.strictEqual(NEW.SWIM_LANG_, 'el');
// Και το lang του αγώνα μόνο του (χωρίς lang στο αίτημα), και μόνο 'en' ακριβώς.
assert.strictEqual(NEW.swimLangOf_({ race: { lang: 'en' } }), 'en');
assert.strictEqual(NEW.swimLangOf_({ lang: 'el', race: { lang: 'en' } }), 'el');
assert.strictEqual(NEW.swimLangOf_({ lang: 'EN' }), 'el');
assert.strictEqual(NEW.swimLangOf_({}), 'el');
// Το μενού: το report ξανά στη γλώσσα του αγώνα όπως κρατήθηκε (αγγλικά), τα μηνύματα του φύλλου ελληνικά.
const sumNow = NEW._ss.getSheetByName('Αναλύσεις Αγώνων');
const rowOf = id => sumNow.getRange(2, 1, sumNow.getLastRow() - 1, 23).getValues().findIndex(row => row[21] === id) + 2;
sumNow._select([[rowOf('lang-full'), 1]]);
NEW.swimRebuildSelected();
assert.strictEqual(NEW._ui._alerts.pop(), 'Η ανάλυση δημιουργήθηκε ξανά.');
const fullSheet = envNew.reports.getSheets().find(s => s.getSheetId() === Number(NEW.swimStoredRace_('lang-full').sheetId));
assert.ok(cellsOf(fullSheet).indexOf('Times & Strokes (Timing Data)') >= 0, 'menu: report in the language of the race');
sumNow._select([[rowOf('lang-warn'), 1]]);
NEW.swimReportsSelected();
assert.ok(/^Report: 1\.$/.test(NEW._ui._alerts.pop()));
assert.ok(cellsOf(sheetOf(r)).indexOf('Χρόνοι & Χεριές (Timing Data)') >= 0, 'menu: Greek race stays Greek');
assert.strictEqual(NEW.SWIM_LANG_, 'el', 'Greek after the menu');
console.log('test_lang (d): the language only for its request; menu re-renders in the language of the race');
