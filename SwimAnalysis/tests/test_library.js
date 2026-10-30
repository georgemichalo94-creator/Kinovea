// Βιβλιοθήκη παλιών αναλύσεων: φόρτωση για έλεγχο, ταίριασμα με το μητρώο, διορθώσεις, καταχώριση, PB, elite αναφορές, report/PDF.
// Όλοι οι αθλητές και οι διοργανώσεις είναι φανταστικοί.
const { makeContext } = require('./mock_gas');
const assert = require('assert');

const athletes = [
  ['unique_id', 'ΟΝΟΜΑΤΕΠΩΝΥΜΟ', 'ΦΥΛΟ', 'ΕΤΟΣ ΓΕΝΝΗΣΗΣ', 'ΗΛΙΚΙΑ', 'ΚΑΤΗΓΟΡΙΑ', 'ΣΩΜΑΤΕΙΟ', 'EMAIL ΠΡΟΠΟΝΗΤΗ'],
  [910001, 'ΔΟΚΙΜΑΚΗΣ ΠΕΤΡΟΣ', 'Ανδρών', 2007, 19, 'Ανδρών', 'Ο.Κ. ΔΟΚΙΜΩΝ', ''],
  [910002, 'ΔΟΚΙΜΑΚΗ ΕΛΕΝΗ', 'Γυναικών', 2009, 17, 'Νεανίδων', 'Ν.Ο. ΠΑΡΑΔΕΙΓΜΑΤΩΝ', ''],
  [910003, 'ΥΠΟΘΕΤΙΚΟΣ ΚΩΝΣΤΑΝΤΙΝΟΣ', 'Ανδρών', 2006, 20, 'Ανδρών', 'Ο.Κ. ΔΟΚΙΜΩΝ', ''],
  [910004, 'ΠΑΡΑΔΕΙΓΜΑΤΟΣ ΓΕΩΡΓΙΟΣ', 'Ανδρών', 2008, 18, 'Εφήβων', 'Ο.Κ. ΔΟΚΙΜΩΝ', ''],
  [910005, 'ΠΑΡΑΔΕΙΓΜΑΤΟΣ ΓΡΗΓΟΡΙΟΣ', 'Ανδρών', 2010, 16, 'Παίδων', 'Ο.Κ. ΔΟΚΙΜΩΝ', ''],
];

// ---- ονόματα
const N = makeContext({ athletes });
assert.strictEqual(N.swimNameMatch_('Δοκιμάκης Πέτρος', 'ΔΟΚΙΜΑΚΗΣ ΠΕΤΡΟΣ'), 2);
assert.strictEqual(N.swimNameMatch_('ΠΕΤΡΟΣ ΔΟΚΙΜΑΚΗΣ', 'Δοκιμάκης Πέτρος'), 2, 'any order');
assert.strictEqual(N.swimNameMatch_('Υποθετικός Κώστας', 'ΥΠΟΘΕΤΙΚΟΣ ΚΩΝΣΤΑΝΤΙΝΟΣ'), 2, 'nickname');
assert.strictEqual(N.swimNameMatch_('ΚΩΝ/ΝΟΣ ΥΠΟΘΕΤΙΚΟΣ', 'Υποθετικός Κωνσταντίνος'), 2, 'Κων/νος');
assert.strictEqual(N.swimNameMatch_('Δοκιμάκη Ε.', 'ΔΟΚΙΜΑΚΗ ΕΛΕΝΗ'), 1, 'initial');
assert.strictEqual(N.swimNameMatch_('Υποθετικός Κωνσταντίνος', 'ΥΠΟΘΕΤΙΚΟΣ ΚΩΝΣΤΑΝΤΙΝΟΣ-ΑΓΓΕΛΟΣ'), 1, 'fewer first names');
assert.strictEqual(N.swimNameMatch_('Δοκιμάκης Πέτρος', 'ΔΟΚΙΜΑΚΗ ΕΛΕΝΗ'), 0);
assert.strictEqual(N.swimNameMatch_('Π. Δ.', 'ΔΟΚΙΜΑΚΗΣ ΠΕΤΡΟΣ'), 0, 'initials alone are not enough');
assert.deepStrictEqual(JSON.parse(JSON.stringify(N.swimLibraryFixes_('150=111.62; 185=2:19,04\n95=; x'))), { 150: 111.62, 185: 139.04, 95: null });

// ---- το αρχείο της βιβλιοθήκης (φανταστικό)
const r2 = x => Math.round(x * 100) / 100;
function freeTimes(splits, offsets) {
  // χρόνοι στα 15/25/35/45/50 κάθε 50άρι από τα 50άρια και τα ποσοστά τους
  const cps = []; let t0 = 0;
  splits.forEach((s, i) => {
    const b = i * 50;
    offsets.forEach(([d, f]) => cps.push({ d: b + d, t: r2(t0 + s * f) }));
    t0 = r2(t0 + s);
    cps.push({ d: b + 50, t: t0 });
  });
  return cps;
}
const base = {
  gender: '', foreign: false, country: '', team: false, relayLeg: '', dq: false, relay: false, pool: 50, competitionRaw: '', location: 'Δοκιμούπολη',
  roundRaw: '', manual: {}, origin: 'excel', action: 'import', drop: [], suspect: [], flags: [], notes: [], versions: [],
  sources: { chosen: { path: 'Δοκιμές/Δοκιμές.xlsm', sheet: 'φύλλο' }, sheets: ['Δοκιμές.xlsm › φύλλο'], pdfs: ['φύλλο.pdf'] }
};
const strokes100 = { 15: 1, 25: 8.5, 35: 9, 45: 9, 50: 4, 65: 3, 75: 9, 85: 9.5, 95: 9.5, 100: 4 };
const races = [
  Object.assign({}, base, { id: 'lib-A', athlete: 'Δοκιμάκης Πέτρος', athleteVariants: ['ΔΟΚΙΜΑΚΗΣ Π.', 'Υποθετικός Κώστας'], style: 'fs', distance: 100, distanceLabel: '100',
    competition: 'Δοκιμαστικό Κύπελλο 2025', round: 'Τελικός', date: '20.12.2025', final: 55.12,
    checkpoints: [[15, 6.4], [25, 11.95], [35, 17.62], [45, 23.31], [50, 26.05], [65, 33.4], [75, 39.2], [85, 45.1], [95, 51.2], [100, 55.12]].map(([d, t]) => ({ d, t })),
    segmentStrokes: strokes100, notes: ['Σημείωση δοκιμής.'],
    manual: { reaction: 0.25, blockTime: 0.7, entryTime: 0.98, entryDistHands: 3.1, entryDistHips: 2.8, underwaterTime: 4.9, underwaterDist: 12.4, breakoutDist: 12.4,
      turns: { 50: { turnTime: 0.78, underwaterDist: 9.6, underwaterTime: 3.9 } }, finishLastStrokeTime: 0.31, finishLastStrokeDist: 0.4 } }),
  Object.assign({}, base, { id: 'lib-J', athlete: 'Δοκιμάκης Πέτρος', athleteVariants: [], style: 'fs', distance: 100, distanceLabel: '100',
    competition: 'Δοκιμαστικό Πρωτάθλημα 2025', round: 'Τελικός', date: '17.5.2025', final: 56.4,
    checkpoints: [[15, 6.5], [25, 12.15], [35, 17.9], [45, 23.7], [50, 26.5], [65, 33.95], [75, 39.85], [85, 45.85], [95, 52.1], [100, 56.4]].map(([d, t]) => ({ d, t })),
    segmentStrokes: strokes100 }),
  Object.assign({}, base, { id: 'lib-F', athlete: 'Δοκιμάκης Πέτρος', athleteVariants: [], style: 'fs', distance: 100, distanceLabel: '100', relayLeg: 'ΣΚΥΤΑΛΗ 4Χ100 ΔΟΚΙΜΗΣ',
    competition: 'Δοκιμαστικό Κύπελλο 2025', round: 'Τελικός', date: '21.12.2025', final: 54.6,
    checkpoints: [[15, 6.1], [25, 11.6], [35, 17.25], [45, 22.95], [50, 25.7], [65, 33.0], [75, 38.8], [85, 44.7], [95, 50.75], [100, 54.6]].map(([d, t]) => ({ d, t })),
    segmentStrokes: strokes100 }),
  Object.assign({}, base, { id: 'lib-G', athlete: 'Δοκιμάκης Πέτρος', athleteVariants: [], style: 'fs', distance: 50, distanceLabel: '50', dq: true,
    competition: 'Δοκιμαστικό Κύπελλο 2025', round: 'Προκριματικοί', date: '19.12.2025', final: 24.9,
    checkpoints: [[15, 6.1], [25, 11.4], [35, 16.8], [45, 22.25], [50, 24.9]].map(([d, t]) => ({ d, t })), segmentStrokes: { 15: 2, 25: 9, 35: 9.5, 45: 9.5, 50: 5 } }),
  Object.assign({}, base, { id: 'lib-B', athlete: 'Υποθετικός Κώστας', athleteVariants: ['ΥΠΟΘΕΤΙΚΟΣ Κ.'], style: 'fs', distance: 400, distanceLabel: '400',
    competition: 'Δοκιμαστικό Κύπελλο 2025', round: 'Τελικός', date: '20.12.2025', strokesPer50: true,
    checkpoints: freeTimes([28.9, 31.6, 31.4, 31.6, 31.7, 31.5, 31.6, 30.9], [[15, 0.272], [25, 0.48], [45, 0.885]]).filter(c => c.d % 50 !== 35),
    segmentStrokes: { 50: 33, 100: 36, 150: 37, 200: 37, 250: 38, 300: 38, 350: 38, 400: 39 },
    drop: [115, 125], suspect: [115, 125], action: 'dropSuspect',
    flags: ['Οι χρόνοι στα 115, 125 είναι ίδιοι με άλλη ανάλυση και χαλάνε τον ρυθμό εδώ. Προτείνεται να μπουν χωρίς αυτούς τους χρόνους.'] }),
  Object.assign({}, base, { id: 'lib-C', athlete: 'Παραδείγματος Γ.', athleteVariants: [], style: 'fly', distance: 50, distanceLabel: '50',
    competition: 'Δοκιμαστική Συνάντηση 2025', round: 'Test event', date: '6.4.2025', final: 27.4,
    checkpoints: [[15, 6.9], [25, 12.6], [35, 18.5], [45, 24.6], [50, 27.4]].map(([d, t]) => ({ d, t })), segmentStrokes: { 15: 1, 25: 6, 35: 6, 45: 6.5, 50: 3 } }),
  Object.assign({}, base, { id: 'lib-D', athlete: 'Jane TESTER', athleteVariants: [], foreign: true, country: 'Testland', style: 'br', distance: 200, distanceLabel: '200',
    competition: 'Δοκιμαστικό Κύπελλο 2025', round: 'Τελικός', date: '21.12.2025',
    checkpoints: freeTimes([33.2, 36.8, 37.5, 38.3], [[15, 0.3], [25, 0.49], [35, 0.69], [45, 0.895]]),
    segmentStrokes: { 15: 2, 25: 5, 35: 5, 45: 5, 50: 3, 65: 4, 75: 5, 85: 5, 95: 5, 100: 3, 115: 4, 125: 5, 135: 5, 145: 5, 150: 3, 165: 4, 175: 5, 185: 5.5, 195: 5.5, 200: 3 } }),
  Object.assign({}, base, { id: 'lib-E', athlete: 'Δοκιμάκη Ελένη', athleteVariants: [], style: 'fs', distance: 200, distanceLabel: '200', action: 'skip',
    competition: 'Δοκιμαστική Συνάντηση 2025', round: 'Test event', date: '6.4.2025',
    checkpoints: freeTimes([30.5, 33.2, 33.6, 33.2], [[15, 0.27], [25, 0.48], [35, 0.68], [45, 0.89]]), segmentStrokes: {},
    flags: ['Το φύλλο αποτελείται από χρόνους άλλων αναλύσεων: δεν είναι ξεχωριστός αγώνας.'] }),
  Object.assign({}, base, { id: 'lib-H', athlete: 'Άγνωστος Κολυμβητής', athleteVariants: [], style: 'bk', distance: 50, distanceLabel: '50',
    competition: 'Δοκιμαστική Συνάντηση 2025', round: 'Test event', date: '6.4.2025', final: 30.2,
    checkpoints: [[15, 8.0], [25, 14.6], [35, 21.0], [45, 27.3], [50, 30.2]].map(([d, t]) => ({ d, t })), segmentStrokes: {} }),
  Object.assign({}, base, { id: 'lib-I', athlete: 'Εθνική Ομάδα Δοκιμής', athleteVariants: [], team: true, relay: true, style: 'im', distance: 400, distanceLabel: '4x100',
    competition: 'Δοκιμαστικό Κύπελλο 2025', round: 'Τελικός', date: '21.12.2025', strokesPer50: true,
    checkpoints: freeTimes([27.6, 29.4, 31.4, 33.6, 27.0, 29.9, 24.8, 26.4], [[15, 0.27], [25, 0.48], [45, 0.89]]),
    segmentStrokes: { 50: 34, 100: 39, 150: 17, 200: 20, 250: 18, 300: 22, 350: 32, 400: 39 } }),
];
races.forEach(r => { if (r.final === undefined) r.final = r.checkpoints[r.checkpoints.length - 1].t; });
const bundle = { app: 'kinovea-swim-library', meta: { version: 1, races: races.length }, races };

const G = makeContext({ athletes });
G.swimShowConnectionInfo = () => {};
G.swimSetup();
const folder = G.swimReportFolder_();
const libFile = folder.createFile(G.Utilities.newBlob(Array.from(Buffer.from(JSON.stringify(bundle), 'utf8')), 'application/json', 'Βιβλιοθήκη αναλύσεων.json'));
const reports = G._all[Object.keys(G._all).find(k => k !== 'DATA')];
const index = reports.getSheetByName('Αγώνες');
const summary = G._ss.getSheetByName('Αναλύσεις Αγώνων');
const L = G.SWIM_LIB;
const W = G.SWIM_LIB_HEADERS.length;

// ---- ο αγώνας από μια εγγραφή: ύποπτοι χρόνοι (φεύγουν με τις χεριές τους) και διορθώσεις (οι χεριές πάνε στο επόμενο σημείο)
const reg = G.swimRegistryIndex_(G.swimAthletes_().athletes);
const rowFor = (action, fix, regId) => { const r = new Array(G.SWIM_LIB_HEADERS.length).fill(''); r[L_.action] = action; r[L_.fix] = fix || ''; r[L_.regId] = regId || ''; return r; };
const L_ = G.SWIM_LIB;
const recX = Object.assign({}, races[0], { drop: [35, 45, 100], action: 'dropSuspect' });
let x = G.swimLibraryRace_(recX, rowFor('Χωρίς τους ύποπτους χρόνους', '', '910001'), reg);
assert.strictEqual(x.checkpoints.map(p => p.d).join(','), '15,25,50,65,75,85,95,100', 'suspect points out, the finish stays');
// 2.15.0: the 25–50 segment that the suspect 35 and 45 left has no count (its 4 strokes were those of 45–50 only).
assert.strictEqual(x.segmentStrokes[35], undefined); assert.strictEqual(x.segmentStrokes[45], undefined); assert.strictEqual(x.segmentStrokes[50], undefined);
assert.strictEqual(x.athlete, 'ΔΟΚΙΜΑΚΗΣ ΠΕΤΡΟΣ'); assert.strictEqual(x.athleteId, '910001'); assert.strictEqual(x.gender, 'M');
x = G.swimLibraryRace_(recX, rowFor('Εισαγωγή', '85=; 95=51.30; 97,5=52.6', ''), reg);
assert.strictEqual(x.checkpoints.map(p => p.d).join(','), '15,25,35,45,50,65,75,95,97.5,100');
assert.strictEqual(x.checkpoints.find(p => p.d === 95).t, 51.3); assert.strictEqual(x.segmentStrokes[95], 19, '85 + 95 strokes');
assert.strictEqual(x.athlete, 'Δοκιμάκης Πέτρος', 'no registry id: the name of the files'); assert.strictEqual(x.athleteId, '');
assert.throws(() => G.swimLibraryRace_(recX, rowFor('Εισαγωγή', '', '123'), reg), /123 δεν υπάρχει/);

// ---- 1. φόρτωση για έλεγχο
G.swimLibraryLoad();
assert.strictEqual(G._props.SWIM_LIBRARY_FILE_ID, libFile.getId(), 'file found by name in the reports folder');
assert.ok(/10 αγώνες/.test(G._ui._alerts.pop()), 'load alert');
const lib = G._ss.getSheetByName('Βιβλιοθήκη (έλεγχος)');
assert.ok(lib, 'review sheet');
const rows = () => lib.getRange(2, 1, lib.getLastRow() - 1, W).getValues();
const rowOf = id => { const i = rows().findIndex(r => r[L.id] === id); assert.ok(i >= 0, 'row of ' + id); return i + 2; };
const get = id => rows().find(r => r[L.id] === id);
const set = (id, col, v) => lib.getRange(rowOf(id), col + 1).setValue(v);
assert.strictEqual(lib.getLastRow(), 11);

let a = get('lib-A');
assert.strictEqual(a[L.on], true); assert.strictEqual(a[L.action], 'Εισαγωγή'); assert.strictEqual(a[L.status], 'Έτοιμο', a[L.check]);
assert.strictEqual(a[L.regId], '910001', 'the stale header of another athlete is not a variant'); assert.strictEqual(a[L.regName], 'ΔΟΚΙΜΑΚΗΣ ΠΕΤΡΟΣ'); assert.strictEqual(a[L.gender], 'Α');
assert.strictEqual(a[L.event], '100 fs'); assert.strictEqual(a[L.pool], '50μ.'); assert.strictEqual(a[L.final], '55.12');
assert.strictEqual(a[L.notes], 'Σημείωση δοκιμής.'); assert.ok(/Δοκιμές\.xlsm › φύλλο \(\+ PDF\)/.test(a[L.source]));
let b = get('lib-B');
assert.strictEqual(b[L.regId], '910003', 'nickname → registry'); assert.strictEqual(b[L.action], 'Χωρίς τους ύποπτους χρόνους');
assert.strictEqual(b[L.status], 'Έλεγχος'); assert.ok(/115, 125/.test(b[L.check]));
assert.strictEqual(b[L.final], G.swimFormatTime_(races[4].final));
let c = get('lib-C');
assert.strictEqual(c[L.regId], ''); assert.strictEqual(c[L.status], 'Έλεγχος');
assert.ok(/Πολλοί αθλητές/.test(c[L.check]) && /910004/.test(c[L.check]) && /910005/.test(c[L.check]), c[L.check]);
let d = get('lib-D');
assert.strictEqual(d[L.elite], true, 'foreign → elite reference'); assert.strictEqual(d[L.regId], ''); assert.ok(/Φύλο/.test(d[L.check]));
assert.ok(!/μητρώο/.test(d[L.check]), 'no registry check for foreign athletes');
let e = get('lib-E');
assert.strictEqual(e[L.on], false); assert.strictEqual(e[L.action], 'Παράλειψη');
assert.ok(/Δεν βρέθηκε στο μητρώο/.test(get('lib-H')[L.check]));
assert.strictEqual(get('lib-F')[L.event], '100 fs σκέλος');
assert.strictEqual(get('lib-I')[L.event], '4x100 medley relay'); assert.ok(/Φύλο/.test(get('lib-I')[L.check]));
// κουτάκια, λίστα ενεργειών, χρώματα
assert.strictEqual(lib._validations[rowOf('lib-A') + ':' + (L.on + 1)].kind, 'checkbox');
assert.strictEqual(lib._validations[rowOf('lib-A') + ':' + (L.elite + 1)].kind, 'checkbox');
assert.strictEqual(JSON.stringify(lib._validations[rowOf('lib-A') + ':' + (L.action + 1)].list), JSON.stringify(G.SWIM_LIB_ACTIONS));
assert.strictEqual(lib.getConditionalFormatRules().length, 3);
assert.strictEqual(lib.getConditionalFormatRules()[0].formula, '=$C2="Έλεγχος"');

// ---- 2. ο έλεγχος: Μητρώο, διορθώσεις χρόνων, φύλο, παράλειψη – και ξαναφόρτωση (κρατά ό,τι συμπληρώθηκε)
set('lib-C', L.regId, '910004');
set('lib-A', L.fix, '50=26.10; 45=');
set('lib-D', L.gender, 'Γ');
set('lib-H', L.on, false);
set('lib-B', L.regId, '999999');
G.swimLibraryLoad_(libFile);
assert.strictEqual(lib.getLastRow(), 11, 'same rows after reload');
c = get('lib-C');
assert.strictEqual(c[L.regId], '910004'); assert.strictEqual(c[L.regName], 'ΠΑΡΑΔΕΙΓΜΑΤΟΣ ΓΕΩΡΓΙΟΣ'); assert.strictEqual(c[L.status], 'Έτοιμο', c[L.check]);
assert.strictEqual(get('lib-A')[L.fix], '50=26.10; 45=');
d = get('lib-D'); assert.strictEqual(d[L.gender], 'Γ'); assert.strictEqual(d[L.status], 'Έτοιμο', d[L.check]);
assert.strictEqual(get('lib-H')[L.on], false);
b = get('lib-B'); assert.ok(/999999 δεν υπάρχει/.test(b[L.check]), b[L.check]); assert.strictEqual(b[L.regName], '');

// ---- 3. καταχώριση: λάθος μητρώο → δεν μπαίνει, τα υπόλοιπα μπαίνουν
let res = G.swimLibraryImport_(libFile);
assert.strictEqual(res.imported, 7, JSON.stringify(res));          // A, J, F, G, C, D, I (το H ξετσεκαρίστηκε, το E παραλείπεται)
assert.strictEqual(res.updated, 0); assert.strictEqual(res.references, 1);
assert.strictEqual(res.errors.length, 1); assert.ok(/999999/.test(res.errors[0]));
b = get('lib-B'); assert.strictEqual(b[L.on], true, 'not imported: stays ticked'); assert.strictEqual(b[L.status], 'Έλεγχος');
a = get('lib-A'); assert.strictEqual(a[L.on], false); assert.ok(/^Καταχωρίστηκε/.test(a[L.status]));
assert.strictEqual(get('lib-E')[L.on], false); assert.strictEqual(get('lib-E')[L.status], 'Έλεγχος');
assert.strictEqual(index.getLastRow(), 8, 'index rows');
const idxRows = () => index.getRange(2, 1, index.getLastRow() - 1, 15).getValues();
const stored = id => { const r = idxRows().find(x => x[0] === id); assert.ok(r, 'index row ' + id); return { row: r, race: JSON.parse(r[14]) }; };
let sa = stored('lib-A');
assert.strictEqual(sa.row[1], 'χωρίς report'); assert.strictEqual(sa.row[2], 'ΔΟΚΙΜΑΚΗΣ ΠΕΤΡΟΣ'); assert.strictEqual(sa.row[3], '910001');
assert.strictEqual(sa.row[4], 'fs'); assert.strictEqual(sa.row[5], '100'); assert.strictEqual(sa.row[9], 55.12); assert.strictEqual(sa.row[12], '', 'no sheet');
assert.strictEqual(sa.race.club, 'Ο.Κ. ΔΟΚΙΜΩΝ'); assert.strictEqual(sa.race.category, 'Ανδρών'); assert.strictEqual(sa.race.gender, 'M');
assert.strictEqual(sa.race.source, 'excel'); assert.strictEqual(sa.race.compare.pb, 'auto'); assert.strictEqual(sa.race.location, 'Δοκιμούπολη');
const at = (race, d) => (race.checkpoints.find(x => x.d === d) || {}).t;
assert.strictEqual(at(sa.race, 50), 26.1, 'time correction'); assert.strictEqual(at(sa.race, 45), undefined, 'checkpoint removed by correction');
assert.strictEqual(sa.race.manual.turns['50'].underwaterDist, 9.6); assert.strictEqual(sa.race.manual.reaction, 0.25);
assert.strictEqual(sa.race.segmentStrokes['25'], 8.5);
assert.strictEqual(sa.race.segmentStrokes['50'], 13, 'strokes of the removed point go to the next one'); assert.strictEqual(sa.race.segmentStrokes['45'], undefined);
const sc = stored('lib-C');
assert.strictEqual(sc.row[2], 'ΠΑΡΑΔΕΙΓΜΑΤΟΣ ΓΕΩΡΓΙΟΣ', 'registry name from the chosen id'); assert.strictEqual(sc.row[3], '910004');
assert.strictEqual(stored('lib-F').row[4], 'fs σκέλος'); assert.strictEqual(stored('lib-F').race.relayLeg, 'ΣΚΥΤΑΛΗ 4Χ100 ΔΟΚΙΜΗΣ');
assert.strictEqual(stored('lib-G').race.dq, true);
const sd = stored('lib-D');
assert.strictEqual(sd.row[2], 'Jane TESTER'); assert.strictEqual(sd.row[3], ''); assert.strictEqual(sd.race.club, 'Testland'); assert.strictEqual(sd.race.gender, 'F');
const si = stored('lib-I');
assert.strictEqual(si.row[4], 'medley relay'); assert.strictEqual(si.row[5], '4x100'); assert.strictEqual(si.race.strokesPer50, true);
// στο "Αναλύσεις Αγώνων"
const sumRows = () => summary.getRange(2, 1, summary.getLastRow() - 1, 23).getValues();
const sumOf = id => sumRows().find(r => r[21] === id);
assert.strictEqual(summary.getLastRow(), 8);
let s = sumOf('lib-A');
assert.strictEqual(s[0], 910001); assert.strictEqual(s[1], 'ΔΟΚΙΜΑΚΗΣ ΠΕΤΡΟΣ'); assert.strictEqual(s[2], 'Ο.Κ. ΔΟΚΙΜΩΝ'); assert.strictEqual(s[4], '20.12.2025');
assert.strictEqual(s[5], 'Δοκιμαστικό Κύπελλο 2025'); assert.strictEqual(s[8], 'fs'); assert.strictEqual(s[10], '50μ.'); assert.strictEqual(s[11], 55.12);
assert.ok(s[14] > 40 && s[14] < 60, 'SR ' + s[14]); assert.ok(s[15] > 0.9 && s[15] < 1.4, 'DPS ' + s[15]); assert.ok(s[16] > 10 && s[16] < 17, 'ΔΑΚ ' + s[16]);
assert.strictEqual(s[17], 6.4); assert.strictEqual(s[18], '26.10 / 29.02'); assert.strictEqual(s[19], '', 'no report yet');
assert.strictEqual(sumOf('lib-G')[8], 'fs (DQ)'); assert.strictEqual(sumOf('lib-F')[8], 'fs σκέλος');
assert.strictEqual(sumOf('lib-D')[2], 'Testland'); assert.strictEqual(sumOf('lib-D')[0], '');
s = sumOf('lib-I'); assert.ok(s[14] > 20 && s[14] < 60, 'whole-50 SR for strokes per 50: ' + s[14]);
// elite αναφορά
const refs = G.swimReferences_().references;
assert.strictEqual(refs.length, 1);
assert.strictEqual(refs[0].name, 'Jane TESTER – Δοκιμαστικό Κύπελλο 2025 Τελικός'); assert.strictEqual(refs[0].raceId, 'lib-D');
assert.strictEqual(G.swimGenderCode_(refs[0].gender), 'F'); assert.strictEqual(refs[0].style, 'br'); assert.strictEqual(refs[0].distance, 200);
assert.strictEqual(refs[0].source, 'Δοκιμαστικό Κύπελλο 2025 · Τελικός · 21.12.2025');
assert.strictEqual(refs[0].times.length, races[6].checkpoints.length); assert.strictEqual(refs[0].times[refs[0].times.length - 1].t, races[6].final);
// PB: οι αγώνες της βιβλιοθήκης φαίνονται στις αναλύσεις του αθλητή (όχι το σκέλος σκυταλοδρομίας, όχι το DQ)
let pbs = G.swimAnalysesFor_({ athleteId: '910001', style: 'fs', distance: 100, pool: 50, poolUnit: 'm' });
assert.strictEqual(pbs.map(x => x.id).join(','), 'lib-A,lib-J');
assert.strictEqual(G.swimAnalysesFor_({ athleteId: '910001', style: 'fs', distance: 50, pool: 50, poolUnit: 'm' }).length, 0, 'DQ not a PB');
assert.strictEqual(G.swimAutoPb_(G.swimNormalizeRace_(sa.race)), 'lib-J', 'PB before the race');

// ---- 4. διόρθωση του μητρώου του B και νέα καταχώριση: μπαίνει χωρίς τους ύποπτους χρόνους
set('lib-B', L.regId, '910003');
res = G.swimLibraryImport_(libFile);
assert.strictEqual(res.imported, 1, JSON.stringify(res)); assert.strictEqual(res.errors.length, 0);
const sb = stored('lib-B');
assert.strictEqual(sb.row[2], 'ΥΠΟΘΕΤΙΚΟΣ ΚΩΝΣΤΑΝΤΙΝΟΣ');
assert.strictEqual(at(sb.race, 115), undefined); assert.strictEqual(at(sb.race, 125), undefined, 'suspect times dropped');
assert.strictEqual(at(sb.race, 100), races[4].checkpoints.find(x => x.d === 100).t); assert.strictEqual(at(sb.race, 400), races[4].final);
assert.strictEqual(sb.race.strokesPer50, true);
s = sumOf('lib-B'); assert.strictEqual(s[11], races[4].final); assert.ok(s[14] > 25 && s[14] < 45, 'whole-50 SR ' + s[14]); assert.ok(s[15] > 1.2 && s[15] < 1.8, 'DPS ' + s[15]);

// ---- 5. ξαναφόρτωση: όσοι μπήκαν φαίνονται "Στη βιβλιοθήκη" και δεν είναι τσεκαρισμένοι
G.swimLibraryLoad();
assert.ok(/8 είναι ήδη στη βιβλιοθήκη/.test(G._ui._alerts.pop()));
a = get('lib-A'); assert.strictEqual(a[L.status], 'Στη βιβλιοθήκη'); assert.strictEqual(a[L.on], false);
assert.strictEqual(get('lib-H')[L.status], 'Έλεγχος'); assert.strictEqual(get('lib-H')[L.on], false);

// ---- 6. νέα καταχώριση του ίδιου αγώνα με άλλη διόρθωση: ενημέρωση, όχι διπλή γραμμή
set('lib-A', L.on, true); set('lib-A', L.fix, '50=26.20');
res = G.swimLibraryImport_(libFile);
assert.strictEqual(res.imported, 1); assert.strictEqual(res.updated, 1);
assert.strictEqual(index.getLastRow(), 9); assert.strictEqual(summary.getLastRow(), 9);
sa = stored('lib-A'); assert.strictEqual(at(sa.race, 50), 26.2); assert.strictEqual(at(sa.race, 45), 23.31, 'checkpoint back without the old correction');

// ---- 7. report για τις επιλεγμένες γραμμές του "Αναλύσεις Αγώνων"
const rowSum = id => sumRows().findIndex(r => r[21] === id) + 2;
summary._select([[rowSum('lib-A'), 1], [rowSum('lib-C'), 1]]);
G.swimReportsSelected();
assert.ok(/^Report: 2/.test(G._ui._alerts.pop()));
sa = stored('lib-A');
assert.ok(sa.row[12], 'sheet id after report'); assert.notStrictEqual(sa.row[1], 'χωρίς report');
const rep = reports.getSheets().find(x => String(x.getSheetId()) === String(sa.row[12]));
assert.ok(rep && /^ΔΟΚΙΜΑΚΗΣ ΠΕΤΡΟΣ 100fs/.test(rep.getName()), rep && rep.getName());
assert.ok(/#gid=/.test(sumOf('lib-A')[19]), 'report link in the summary');
assert.ok(stored('lib-C').row[12]);
assert.strictEqual(summary.getLastRow(), 9, 'report does not add summary rows');
// PDF για αγώνα χωρίς report: φτιάχνεται πρώτα το φύλλο
summary._select([[rowSum('lib-D'), 1]]);
G.swimPdfSelected();
assert.ok(stored('lib-D').row[12], 'sheet made for the PDF');
assert.ok(/^https:\/\/drive\.google\.com\/file\/d\//.test(sumOf('lib-D')[20]), 'pdf link');
assert.strictEqual(G._ui._dialogs.pop().title, 'Report PDF');
// επιλογή σε άλλο φύλλο
lib._select([[2, 1]]);
G.swimReportsSelected();
assert.ok(/Επίλεξε γραμμές στο φύλλο "Αναλύσεις Αγώνων"/.test(G._ui._alerts.pop()));

// ---- 8. μενού και οδηγίες
G.swimOnOpen();
const menu = G._ui._menus.pop();
const fns = [];
(function walk(m) { m.items.forEach(it => (it.sub ? walk(it.sub) : fns.push(it.fn))); })(menu);
fns.forEach(fn => assert.strictEqual(typeof G[fn], 'function', 'menu function ' + fn));
['swimLibraryLoad', 'swimLibraryImport', 'swimOpenLibrary', 'swimReportsSelected'].forEach(fn => assert.ok(fns.includes(fn), fn));
G.swimOpenLibrary();
assert.ok(/βρέθηκε/.test(G._ui._dialogs.pop().html));

// ---- 9. λιγότεροι αγώνες στο αρχείο: οι γραμμές που περισσεύουν καθαρίζουν (και τα κουτάκια τους)
const small = { app: 'kinovea-swim-library', meta: {}, races: races.slice(0, 3) };
const smallFile = folder.createFile(G.Utilities.newBlob(Array.from(Buffer.from(JSON.stringify(small), 'utf8')), 'application/json', 'small.json'));
G.swimLibraryLoad_(smallFile);
assert.strictEqual(lib.getLastRow(), 4);
assert.ok(!lib._validations['8:' + (L.on + 1)], 'no checkbox left on empty rows');
assert.ok(lib._validations['3:' + (L.on + 1)], 'checkboxes on the rows in use');

// ---- 10. το αρχείο: σύνδεσμος του Drive όταν δεν βρίσκεται στον φάκελο, και λάθος αρχείο
const H = makeContext({ athletes });
H.swimShowConnectionInfo = () => {};
H.swimSetup();
const other = H._drive._root.createFolder('Άλλος φάκελος').createFile(H.Utilities.newBlob(Array.from(Buffer.from(JSON.stringify(bundle), 'utf8')), 'application/json', 'lib.json'));
H._ui._answer = null;
assert.strictEqual(H.swimLibraryFile_(true), null, 'cancel');
assert.strictEqual(H.swimLibraryFile_(false), null);
H._ui._answer = 'https://drive.google.com/file/d/' + other.getId() + '/view?usp=sharing';
assert.strictEqual(H.swimLibraryFile_(true).getId(), other.getId(), 'link pasted in the prompt');
assert.strictEqual(H._props.SWIM_LIBRARY_FILE_ID, other.getId());
assert.strictEqual(H.swimLibraryFile_(false).getId(), other.getId(), 'remembered');
const bad = H._drive._root.createFile(H.Utilities.newBlob(Array.from(Buffer.from('{"app":"x","races":[]}', 'utf8')), 'application/json', 'bad.json'));
assert.throws(() => H.swimLibraryLoad_(bad), /δεν είναι αρχείο βιβλιοθήκης/);
assert.throws(() => H.swimLibraryImport_(other), /Φόρτωση για έλεγχο/, 'import before load');

console.log('library ok');
