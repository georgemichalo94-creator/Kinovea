// 2.15.0 (έλεγχος εγκυρότητας): τιμές μόνο όπου μετρήθηκαν και από το σωστό ρολόι. Ό,τι μετριέται στο βίντεο (στροφή, τελευταία
// χεριά, πτήση, χεριές) από τα σημάδια του βίντεο όταν ο επίσημος χρόνος ενός σημείου είναι λίγο μακριά από το καρέ του· ο
// υποβρύχιος χρόνος μόνο από την αποκόλληση· κανένας δελφινισμός = κενό· τα κελιά τιμών που δεν υπάρχουν πια σβήνονται· οι
// αποστάσεις που εκτιμήθηκαν από τους χρόνους με ≈ και χωρίς ταχύτητα. Φανταστικοί αθλητές.
const { makeContext } = require('./mock_gas');
const assert = require('assert');
const athletes = [
  ['unique_id', 'ΟΝΟΜΑΤΕΠΩΝΥΜΟ', 'ΦΥΛΟ', 'ΕΤΟΣ ΓΕΝΝΗΣΗΣ', 'ΗΛΙΚΙΑ', 'ΚΑΤΗΓΟΡΙΑ', 'ΣΩΜΑΤΕΙΟ', 'EMAIL ΠΡΟΠΟΝΗΤΗ'],
  [900001, 'ΑΛΦΑ ΔΟΚΙΜΗ', 'Γυναικών', 2004, 22, 'Γυναικών', 'Ο.Κ. ΔΟΚΙΜΩΝ', ''],
  ['', '', '', '', '', '', '', ''],
];
const G = makeContext({ athletes });
const call = body => JSON.parse(G.swimDoPost({ postData: { contents: JSON.stringify(body) } }).text);
G.swimShowConnectionInfo = () => {};
G.swimSetup();
const near = (a, b, what) => assert.ok(a !== null && a !== undefined && Math.abs(a - b) < 1e-6, what + ': ' + a + ' (expected ' + b + ')');

// 100μ. πρόσθιο: ο επίσημος χρόνος στα 50 0.12 s μετά το σημάδι του τοίχου (28.00), ο τελικός 0.08 s μετά το σημάδι (57.62),
// ο επίσημος χρόνος αντίδρασης 0.64 (η αποδέσμευση μαρκαρίστηκε στα 0.70).
const times = { 15: 6.5, 25: 12.5, 35: 18.6, 45: 24.7, 50: 28.12, 65: 35.3, 75: 41.5, 85: 47.8, 95: 54.1, 100: 57.70 };
const marks = { 50: 28.00, 100: 57.62 };
function race() {
  return {
    id: 'strict1', athlete: 'ΑΛΦΑ ΔΟΚΙΜΗ', athleteId: '900001', style: 'br', distance: 100, pool: 50, round: 'Τελικός', date: '29.10.2026',
    checkpoints: Object.keys(times).map(Number).map(d => marks[d] !== undefined ? { d, t: times[d], v: marks[d] } : { d, t: times[d] }),
    strokes: [7.1, 8.2, 9.3, 10.4, 11.5, 12.6, 13.7, 14.8, 15.9, 17.0, 18.1, 19.2, 20.3, 21.4, 22.5, 23.6, 24.7, 25.8, 27.20,
      32.0, 33.1, 34.2, 35.3, 36.4, 37.5, 38.6, 39.7, 40.8, 41.9, 43.0, 44.1, 45.2, 46.3, 47.4, 48.5, 49.6, 50.7, 51.8, 52.9, 54.0, 55.1, 56.80],
    kicks: [2.0, 2.5, 29.3], breakouts: [5.0, 31.4], feetOff: [0.64, 28.85], releaseMark: 0.70, entries: [1.02],
    manual: { reaction: 0.25, underwaterDist: 11.5, turns: { '50': { underwaterDist: 8.4 } } }
  };
}

// (a) η ανάλυση: τα γεγονότα του βίντεο από τα σημάδια, οι χρόνοι των σημείων από τους επίσημους
let a = G.swimAnalyze_(G.swimNormalizeRace_(race()));
const t50 = a.turns[0];
near(t50.turnTime, 0.85, 'open turn: feet off − the wall mark (not − the official 28.12: 0.73)');
near(t50.lastStrokeGap, 0.80, 'last stroke → the wall mark (not → 28.12: 0.92)');
near(t50.underwaterTime, 2.55, 'underwater time B − F');
assert.strictEqual(t50.kicks, 1, 'one kick between the wall and the breakout');
near(t50.out15, 35.3 - 28.12, 'wall → +15 with the official time of the wall');
near(a.finish.lastStrokeGap, 0.82, 'last stroke → the finish mark (not → 57.70: 0.90)');
near(a.start.blockTime, 0.64, 'block: the official reaction time');
near(a.start.flightTime, 0.32, 'flight: entry − the marked release (not − the official one: 0.38)');
assert.strictEqual(a.start.kicks, 2, 'start kicks');
assert.strictEqual(a.rows.find(r => r.d === 50).t, 28.12, 'the checkpoint keeps its official time');

// (b) τιμές μόνο όπου μετρήθηκαν: χωρίς αποκόλληση κανένας υποβρύχιος χρόνος (όχι B − S)· χωρίς δελφινισμούς στη στροφή κενό, όχι 0
const r2 = race();
r2.feetOff = [0.64];
r2.kicks = [2.0, 2.5];
a = G.swimAnalyze_(G.swimNormalizeRace_(r2));
assert.strictEqual(a.turns[0].underwaterTime, null, 'no underwater time without the push-off');
assert.strictEqual(a.turns[0].turnTime, null, 'no turn time without the push-off');
assert.strictEqual(a.turns[0].kicks, null, 'no kicks marked in the turn: blank, not 0');
assert.strictEqual(a.start.kicks, 2, 'start kicks still counted');

// (c) το φύλλο: τα κελιά του προγράμματος σβήνονται όταν η τιμή δεν υπάρχει πια
const sheetOf = res => {
  const rep = G._all[Object.keys(G._all).find(k => k !== 'DATA')];
  return rep.getSheets().find(s => s.getName() === res.sheetName);
};
const below = (sh, header, nth) => {
  const vals = sh.getRange(1, 1, sh.getLastRow(), 15).getValues();
  const hits = [];
  vals.forEach((row, i) => row.forEach((v, j) => { if (v === header) hits.push([i, j]); }));
  assert.ok(hits.length > (nth || 0), 'header ' + header);
  const [i, j] = hits[nth || 0];
  return vals[i + 1][j];
};
let res = call({ token: 'tok', action: 'update', race: race() });
assert.ok(res.ok, JSON.stringify(res));
let sh = sheetOf(res);
near(Number(below(sh, 'Χρόνος στροφής (s)')), 0.85, 'turn time in the sheet');
near(Number(below(sh, 'Χρόνος κάτω από το νερό (s)')), 2.55, 'underwater time in the sheet');
near(Number(below(sh, 'Τελευταία χεριά → τοίχος (s)')), 0.80, 'last stroke → wall in the sheet');
near(Number(below(sh, 'Πτήση: βατήρας → είσοδος (sec)')), 0.32, 'flight in the sheet');
near(Number(below(sh, 'Μέση ταχύτητα από το σήμα έως την έξοδο (m/s)')), 11.5 / 5.0, 'signal → breakout speed from a measured distance');

const r3 = race();
r3.feetOff = [0.64];           // the push-off of the turn deleted (e.g. a wrong mark of the pilot)
r3.breakouts = [5.0];          // and its breakout
r3.kicks = [2.0, 2.5];
res = call({ token: 'tok', action: 'update', race: r3 });
assert.ok(res.ok && !res.rebuilt, JSON.stringify(res));
sh = sheetOf(res);
assert.strictEqual(below(sh, 'Χρόνος στροφής (s)'), '', 'turn time cleared');
assert.strictEqual(below(sh, 'Χρόνος κάτω από το νερό (s)'), '', 'underwater time cleared');
assert.strictEqual(below(sh, 'Δελφινισμοί'), '', 'kicks of the turn: blank');

// (d) οι αποστάσεις που εκτιμήθηκαν από τους χρόνους: με ≈, χωρίς ταχύτητα
const r4 = race();
r4.manual.underwaterEstimated = true;
r4.manual.turns['50'].underwaterEstimated = true;
res = call({ token: 'tok', action: 'update', race: r4 });
assert.ok(res.ok, JSON.stringify(res));
sh = sheetOf(res);
assert.strictEqual(below(sh, 'Απόσταση κάτω από το νερό (μ.)'), '≈11.5', 'start: the estimate marked');
assert.strictEqual(below(sh, 'Μέση ταχύτητα από το σήμα έως την έξοδο (m/s)'), '', 'no speed from an estimate');
assert.strictEqual(below(sh, 'Μέτρα κάτω από το νερό (μ.)'), '≈8.4', 'turn: the estimate marked');

// (d1) Kinovea 2.34.0: με μόνο την απόσταση εξόδου μετρημένη (📍· η απόσταση κάτω από το νερό εκτίμηση ή κενή) το πρόγραμμα στέλνει τη
// μέση ταχύτητα σήμα → έξοδος και το κελί τη δείχνει (το script την έβγαζε μόνο από την απόσταση κάτω από το νερό: έμενε κενό).
const r4a = race();
r4a.manual.underwaterEstimated = true;
r4a.manual.breakoutDist = 12.1;
r4a.manual.signalBreakoutVel = 2.42;
res = call({ token: 'tok', action: 'update', race: r4a });
assert.ok(res.ok, JSON.stringify(res));
sh = sheetOf(res);
near(Number(below(sh, 'Μέση ταχύτητα από το σήμα έως την έξοδο (m/s)')), 2.42, 'the speed the program sends with the breakout distance alone');
assert.strictEqual(below(sh, 'Απόσταση κάτω από το νερό (μ.)'), '≈11.5', 'the estimate still marked');
res = call({ token: 'tok', action: 'update', race: r4 });
sh = sheetOf(res);
assert.strictEqual(below(sh, 'Μέση ταχύτητα από το σήμα έως την έξοδο (m/s)'), '', 'without it again: cleared');

// (d2) μια εκτίμηση που δεν υπάρχει πια (Kinovea: underwaterDist null): το ≈ σβήνεται· ό,τι γράφτηκε με το χέρι στο φύλλο μένει
const cellBelow = (sh2, header) => {
  const vals = sh2.getRange(1, 1, sh2.getLastRow(), 15).getValues();
  for (let i = 0; i < vals.length; i++) for (let j = 0; j < vals[i].length; j++) if (vals[i][j] === header) return [i + 2, j + 1];
  return null;
};
const r4b = race();
r4b.manual.underwaterDist = null;
r4b.manual.turns['50'].underwaterDist = null;
res = call({ token: 'tok', action: 'update', race: r4b });
assert.ok(res.ok, JSON.stringify(res));
sh = sheetOf(res);
assert.strictEqual(below(sh, 'Απόσταση κάτω από το νερό (μ.)'), '', 'the start estimate taken away: cleared');
assert.strictEqual(below(sh, 'Μέτρα κάτω από το νερό (μ.)'), '', 'the turn estimate taken away: cleared');
const typed = cellBelow(sh, 'Απόσταση κάτω από το νερό (μ.)');
sh.getRange(typed[0], typed[1]).setValue(10.2);
res = call({ token: 'tok', action: 'update', race: r4b });
sh = sheetOf(res);
assert.strictEqual(below(sh, 'Απόσταση κάτω από το νερό (μ.)'), 10.2, 'a value typed in the sheet stays');

// (d3) Kinovea 2.35.0 / Apps Script 2.20.0: οι ταχύτητες των φάσεων της εκκίνησης σε μια γραμμή κάτω από τα στοιχεία της, η
// ταχύτητα έξοδος → +15 κάθε στροφής σε μια στήλη· οι τιμές του προγράμματος, σβήνουν όταν δεν υπάρχουν πια. Ο πίνακας των χρόνων
// δύο γραμμές πιο κάτω.
const r4c = race();
Object.assign(r4c.manual, { sigEntryVel: 3.17, entryBreakoutVel: 2.15, breakout15Vel: 2.18 });
r4c.manual.turns['50'].uwSpeed = 2.08;
r4c.manual.turns['50'].bo15Speed = 1.91;
res = call({ token: 'tok', action: 'update', race: r4c });
assert.ok(res.ok, JSON.stringify(res));
sh = sheetOf(res);
near(Number(below(sh, 'Ταχύτητα: σήμα → είσοδος χεριών (m/s)')), 3.17, 'start: signal → entry of the hands');
near(Number(below(sh, 'Ταχύτητα: είσοδος → έξοδος (m/s)')), 2.15, 'start: entry → breakout');
near(Number(below(sh, 'Ταχύτητα: έξοδος → 15μ. (m/s)')), 2.18, 'start: breakout → 15');
near(Number(below(sh, 'Υποβρύχια ταχύτητα: αποκόλληση → έξοδος (m/s)')), 2.08, 'turn: push-off → breakout');
near(Number(below(sh, 'Ταχύτητα: έξοδος → +15μ. (m/s)')), 1.91, 'turn: breakout → +15');
const startHead = cellBelow(sh, 'Ταχύτητα: σήμα → είσοδος χεριών (m/s)');
const timesHead = cellBelow(sh, 'Distance (m)');
assert.ok(startHead && timesHead && timesHead[0] - startHead[0] === 4, 'the times two rows below the phase speeds: ' + startHead + ' / ' + timesHead);
const raw = sh.getRange(1, 1, sh.getLastRow(), 15).getValues().map(r => r.join(' ')).join(' ');
assert.ok(!/τυπικό ύψος|0\.27|κεφάλι στην/.test(raw), 'no word on how the speeds are found');
res = call({ token: 'tok', action: 'update', race: race() });
sh = sheetOf(res);
assert.strictEqual(below(sh, 'Ταχύτητα: σήμα → είσοδος χεριών (m/s)'), '', 'the start speeds cleared without the values');
assert.strictEqual(below(sh, 'Ταχύτητα: έξοδος → +15μ. (m/s)'), '', 'the turn speed cleared');

// (e) ένα 50άρι με σημείο χωρίς χρόνο (π.χ. ο πιλότος δεν το βρήκε): χωρίς χεριές, Swolf και ΔΑΚ του 50 (όχι το μισό ως όλο)
const strokes50 = []; for (let t = 4.6; t < 30; t += 0.55) strokes50.push(+t.toFixed(2));
const r5 = { id: 'strict5', athlete: 'ΑΛΦΑ ΔΟΚΙΜΗ', style: 'fs', distance: 50, pool: 50,
  checkpoints: [{ d: 15, t: 6.4 }, { d: 25, t: 11.6 }, { d: 35, t: null }, { d: 45, t: 22.2 }, { d: 50, t: 25.0 }], strokes: strokes50,
  kicks: [], breakouts: [4.4], feetOff: [], entries: [], manual: {} };
a = G.swimAnalyze_(G.swimNormalizeRace_(r5));
assert.strictEqual(a.blocks[0].strokes, null, 'no strokes of the 50 with a segment missing');
assert.strictEqual(a.blocks[0].swolf, null, 'no Swolf');
assert.strictEqual(a.blocks[0].dak, null, 'no ΔΑΚ');
assert.strictEqual(a.summary.totalStrokes, null, 'no total strokes');
r5.checkpoints[2].t = 16.9;
a = G.swimAnalyze_(G.swimNormalizeRace_(r5));
assert.ok(a.blocks[0].strokes > 30 && a.blocks[0].swolf > 50 && a.blocks[0].dak > 0, 'all segments: strokes ' + a.blocks[0].strokes + ', Swolf ' + a.blocks[0].swolf);

// (f) 0 δελφινισμοί σε στροφή μόνο όταν μετρήθηκαν σε άλλη στροφή (στροφή προσθίου χωρίς δελφινισμό), κενό όταν σε καμία
const r6 = { id: 'strict6', athlete: 'ΑΛΦΑ ΔΟΚΙΜΗ', style: 'br', distance: 200, pool: 50,
  checkpoints: [15, 25, 35, 45, 50, 65, 75, 85, 95, 100, 115, 125, 135, 145, 150, 165, 175, 185, 195, 200].map((d, i) => ({ d, t: +(d * 0.62).toFixed(2) })),
  strokes: [], kicks: [2.0, 32.0], breakouts: [5.0, 35.0, 66.0, 97.0], feetOff: [0.64, 31.9, 62.9, 93.9], entries: [1.0], manual: {} };
a = G.swimAnalyze_(G.swimNormalizeRace_(r6));
assert.strictEqual(JSON.stringify(a.turns.map(t => t.kicks)), '[1,0,0]', 'kicks counted in the turns: 0 where none (a turn without a dolphin kick)');
r6.kicks = [2.0];
a = G.swimAnalyze_(G.swimNormalizeRace_(r6));
assert.strictEqual(JSON.stringify(a.turns.map(t => t.kicks)), '[null,null,null]', 'kicks marked only at the start: the turns not counted');
assert.strictEqual(a.start.kicks, 1, 'the start counted');

console.log('test_strict: video clock for video events, values only where measured, stale cells cleared, estimates marked, complete 50s');
