// Swim Analytics 1.0 (ο κεντρικός πίνακας, SwimDashboard.gs) με φανταστικούς αγώνες που στέλνονται όπως από το Kinovea:
//  - ανοίγει μόνο όταν το script εκτελείται ως αυτός που τον ανοίγει (ή ο ιδιοκτήτης) και έχει πρόσβαση στο αρχείο· αλλιώς απλό κείμενο,
//  - μόνο διαβάζει (καμία αλλαγή σε φύλλα, ρυθμίσεις ή αρχεία),
//  - δεν δίνει ποτέ αριθμό μητρώου, έτος γέννησης ή βίντεο,
//  - οι τιμές είναι αυτές της ανάλυσης των αναφορών (swimAnalyze_), με τις τιμές του αναλυτή να υπερισχύουν όπως στο φύλλο,
//  - ο έλεγχος για αλλαγές βλέπει νέους και ενημερωμένους αγώνες, η προσωρινή μνήμη δεν δίνει παλιές τιμές,
//  - η σελίδα (SwimDashboard.html) καλεί μόνο συναρτήσεις που υπάρχουν και δεν φορτώνει ξένο κώδικα.
const { makeContext } = require('./mock_gas');
const fs = require('fs');
const path = require('path');
const vm = require('vm');
const assert = require('assert');
// Οι πίνακες του script είναι άλλου πλαισίου (vm): σύγκριση ως JSON.
const same = (a, b, msg) => assert.strictEqual(JSON.stringify(a), JSON.stringify(b), msg);

const dir = path.join(__dirname, '../GoogleSheet');
const dashSrc = fs.readFileSync(path.join(dir, 'SwimDashboard.gs'), 'utf8');
const webSrc = fs.readFileSync(path.join(dir, 'SwimDashboardWeb.gs'), 'utf8');
const html = fs.readFileSync(path.join(dir, 'SwimDashboard.html'), 'utf8');

function context() {
  const G = makeContext({ cache: true });
  vm.runInContext(dashSrc, G);
  const who = { active: 'coach@example.com', effective: 'coach@example.com' };
  G.Session.getActiveUser = () => ({ getEmail: () => who.active });
  G.Session.getEffectiveUser = () => ({ getEmail: () => who.effective });
  G.HtmlService.createHtmlOutputFromFile = file => {
    const o = { file, meta: {} };
    o.setTitle = t => { o.title = t; return o; };
    o.addMetaTag = (k, v) => { o.meta[k] = v; return o; };
    return o;
  };
  G.ContentService.MimeType.TEXT = 'text';
  return { G, who };
}

// ---- Φανταστικοί αγώνες (σταθερός σπόρος: ίδιοι κάθε φορά)
let seed = 4242;
const rnd = () => (seed = (seed * 1103515245 + 12345) % 2147483648) / 2147483648;
function race(n, o) {
  const pool = o.pool || 50, distance = o.distance || 100, v = o.v || 1.8;
  const cps = [], strokes = [], kicks = [], breakouts = [], feetOff = [0.72], entries = [1.05];
  const pts = pool === 50 ? [15, 25, 35, 45, 50] : [15, 20, 25];
  let t = 0;
  for (let L = 0; L < distance / pool; L++) {
    const T = pool / v * (L === 0 ? 0.92 : 1.03) * (0.99 + rnd() * 0.02);
    pts.forEach(p => cps.push({ d: L * pool + p, t: +(t + T * p / pool).toFixed(2) }));
    for (let s = t + 4 + rnd() * 0.3; s < t + T - 0.4; s += 0.6 + rnd() * 0.05) strokes.push(+s.toFixed(3));
    if (L === 0) {
      breakouts.push(+(3.7 + rnd() * 0.4).toFixed(3));
      for (let k = 0; k < 5; k++) kicks.push(+(1.4 + k * 0.45).toFixed(3));
    } else {
      feetOff.push(+(t + 0.62).toFixed(3));
      breakouts.push(+(t + 3.2 + rnd() * 0.4).toFixed(3));
      for (let k = 0; k < 4; k++) kicks.push(+(t + 1 + k * 0.45).toFixed(3));
    }
    t += T;
  }
  const turns = {};
  for (let w = pool; w < distance; w += pool) turns[w] = { underwaterDist: +(8 + rnd() * 3).toFixed(1) };
  if (o.typedTurn) turns[pool].turnTime = o.typedTurn;
  if (o.estTurn) turns[pool].underwaterEstimated = true;
  return {
    id: 'dash' + n, athlete: 'Κολυμβητής ' + 'ΑΒΓΔΕΖΗΘΙΚΛΜ'[n % 12], athleteId: '99000' + (10 + n), birthYear: '2011', category: 'Παίδες',
    gender: o.gender || 'Ανδρών', club: 'Όμιλος ' + 'ΑΒΓ'[n % 3], style: o.style || 'fs', distance, pool, lane: String(1 + (n % 8)),
    competition: o.comp || 'Διασυλλογικοί Αγώνες (δοκιμή)', location: 'Πισίνα (δοκιμή)', round: o.round || 'Τελικός', date: o.date || '17.10.2026',
    video: 'fictional_clip_' + n + '.mp4', checkpoints: cps, strokes, kicks, breakouts, feetOff, entries,
    manual: Object.assign({ reaction: 0.65, entryDistHands: 3.7, underwaterDist: 10.4, breakoutDist: 10.9, turns }, o.manual || {})
  };
}
const SPECS = [
  {}, { v: 1.75 }, { v: 1.85, typedTurn: 0.81 }, { v: 1.7, estTurn: true, manual: { underwaterEstimated: true, breakoutDist: null } },
  { gender: 'Γυναικών', v: 1.6 }, { gender: 'Γυναικών', v: 1.62, round: 'Προκριματικός' },
  { style: 'bk', v: 1.65 }, { style: 'br', v: 1.42, distance: 200 }, { style: 'fly', v: 1.7, pool: 25, distance: 100 },
  { style: 'im', distance: 200, v: 1.55, comp: 'Χειμερινό Πρωτάθλημα (δοκιμή)', date: '2026-02-14' },
  { distance: 400, v: 1.6, manual: { kicks: 7, underwaterTime: 4.4 } }
];
const RACES = SPECS.map((o, n) => race(n, o));

const { G, who } = context();
G.swimShowConnectionInfo = () => {};
G.swimSetup();
const call = body => JSON.parse(G.swimDoPost({ postData: { contents: JSON.stringify(Object.assign({ token: 'tok', app: 'kinovea-swim' }, body)) } }).text);
RACES.forEach(r => { const res = call({ action: 'update', race: r }); assert.ok(res.ok, JSON.stringify(res).slice(0, 300)); });
const reportsId = G._props.SWIM_REPORTS_SPREADSHEET_ID;
const reports = G._all[reportsId];
const index = reports.getSheetByName('Αγώνες');
assert.ok(index && index.getLastRow() === RACES.length + 1, 'index rows');
// Μια τεχνική ανάλυση (προπόνηση) και μια χαλασμένη γραμμή στο ευρετήριο: δεν είναι κούρσες για σύγκριση.
index.getRange(index.getLastRow() + 1, 1, 1, 15).setValues([['tech1', '', 'Κολυμβητής Τ', '', 'τεχνική fs', '', '', '', '', '', '', '', '', '', JSON.stringify({ kind: 'training', athlete: 'Κολυμβητής Τ', style: 'fs' })]]);
index.getRange(index.getLastRow() + 1, 1, 1, 15).setValues([['broken1', '', 'Κ', '', 'fs', '100', '', '', '', 50, '', '', '', '', '{not json']]);

// Ό,τι πρέπει να μένει ίδιο (μόνο διαβάζει): οι ρυθμίσεις, τα αρχεία, τα φύλλα και τα κελιά τους.
const snapshot = () => JSON.stringify({ props: G._props, files: Object.keys(G._all).sort(),
  sheets: Object.keys(G._all).sort().map(k => G._all[k].getSheets().map(s => [s.getName(), s._cells])) });
const before = snapshot();

// Μόνο απλές τιμές στις απαντήσεις (το google.script.run δεν περνά ημερομηνίες, συναρτήσεις κ.λπ.).
function plain(x, where) {
  if (x === null || typeof x === 'string' || typeof x === 'boolean') return;
  if (typeof x === 'number') { assert.ok(isFinite(x), where + ': not finite'); return; }
  assert.ok(typeof x === 'object' && !(x instanceof Date), where + ': not plain ' + typeof x);
  Object.keys(x).forEach(k => plain(x[k], where + '.' + k));
}
// Τα στοιχεία που δεν πρέπει να φτάνουν ποτέ στη σελίδα.
function noPrivate(obj, where) {
  const s = JSON.stringify(obj);
  RACES.forEach(r => {
    assert.ok(s.indexOf(r.athleteId) < 0, where + ': athleteId');
    assert.ok(s.indexOf(r.video) < 0, where + ': video');
  });
  assert.ok(!/"athleteId"|"birthYear"|"video"|"token"/.test(s), where + ': private key');
  assert.ok(s.indexOf('2011') < 0, where + ': birth year');
}

// ---- Πρόσβαση
let page = G.swimDashboardGet({ parameter: {} });
assert.strictEqual(page.file, 'SwimDashboard', 'page for the user who runs the script');
assert.ok(/Swim Analytics/.test(page.title) && /width=device-width/.test(page.meta.viewport), 'title and viewport');
assert.strictEqual(G.swimDashTryGet({ parameter: {} }), null, 'other pages of the project untouched');
assert.strictEqual(G.swimDashTryGet({ parameter: { page: 'dashboard' } }).file, 'SwimDashboard');
who.active = '';
page = G.swimDashboardGet({ parameter: {} });
assert.ok(typeof page.text === 'string' && !page.file && /User accessing/.test(page.text), 'anonymous: text, no page');
let res = G.swimDashList();
assert.ok(!res.ok && res.denied && !res.races, 'anonymous: no data');
who.active = 'visitor@example.com'; who.effective = 'owner@example.com';
page = G.swimDashboardGet({ parameter: {} });
assert.ok(!page.file, 'deployment that runs as the owner: no page for others');
assert.ok(!G.swimDashList().ok && !G.swimDashRaces(['dash0']).ok && !G.swimDashPoll('').ok, 'deployment that runs as the owner: no data for others');
who.active = 'owner@example.com';
assert.ok(G.swimDashList().ok, 'the owner opens it from any deployment');
who.active = who.effective = 'coach@example.com';
// Χωρίς πρόσβαση στο αρχείο (το Google αρνείται): μήνυμα, χωρίς δεδομένα από την προσωρινή μνήμη.
assert.ok(G.swimDashList().ok);
const openById = G.SpreadsheetApp.openById;
G.SpreadsheetApp.openById = () => { throw new Error('You do not have permission to access the requested document.'); };
res = G.swimDashList();
assert.ok(!res.ok && res.denied && /πρόσβαση/.test(res.error) && !res.races, 'no access to the sheet: ' + JSON.stringify(res));
assert.ok(!G.swimDashRaces(['dash0']).ok, 'no access: no races');
assert.ok(!G.swimDashboardGet({ parameter: {} }).file, 'no access: no page');
G.SpreadsheetApp.openById = openById;

// ---- Η λίστα
res = G.swimDashList();
assert.ok(res.ok, JSON.stringify(res));
plain(res, 'list');
noPrivate(res, 'list');
assert.strictEqual(res.races.length, RACES.length, 'races without the technique analysis and the broken row');
assert.strictEqual(res.user, 'coach@example.com');
assert.strictEqual(res.analysis, G.SWIM_VERSION);
const L0 = res.races.find(r => r.id === 'dash0');
assert.strictEqual(L0.gender, 'M');
assert.strictEqual(res.races.find(r => r.id === 'dash4').gender, 'F');
assert.strictEqual(L0.date, '2026-10-17');
assert.strictEqual(res.races.find(r => r.id === 'dash9').date, '2026-02-14');
assert.strictEqual(L0.updated, '2026-10-09 23:59', 'the mock writes 09/10/2026 23:59');
assert.strictEqual(res.races.find(r => r.id === 'dash8').pool, 25);
assert.ok(L0.finalTime > 50 && L0.finalTime < 60, 'final time ' + L0.finalTime);
assert.ok(!('manual' in L0) && !('rows' in L0), 'the list has no measurements');

// ---- Οι μετρήσεις: ίδιες με την ανάλυση των αναφορών, με τις τιμές του αναλυτή
const r3 = x => x === null || x === undefined ? null : Math.round(x * 1000) / 1000;
res = G.swimDashRaces(RACES.map(r => r.id).concat(['nope', 'tech1']));
assert.ok(res.ok, JSON.stringify(res).slice(0, 300));
plain(res, 'races');
noPrivate(res, 'races');
// 11 αγώνες + 'nope' = τα πρώτα 12 (SWIM_DASH_MAX_RACES): το 'tech1' δεν διαβάζεται καν.
assert.strictEqual(G.SWIM_DASH_MAX_RACES, 12);
assert.strictEqual(res.races.length, RACES.length);
same(res.missing, ['nope'], 'only the first 12 ids are read');
res.races.forEach(f => {
  const raw = RACES.find(r => r.id === f.id);
  const race = G.swimNormalizeRace_(JSON.parse(JSON.stringify(raw)));
  const a = G.swimAnalyze_(race);
  ['finalTime', 'avgVel', 'totalStrokes', 'avgSr', 'avgDps', 'avgDak', 'startTime', 'turnsTime', 'finishTime'].forEach(k =>
    assert.strictEqual(f.summary[k], r3(a.summary[k]), f.id + ' summary.' + k));
  assert.strictEqual(f.rows.length, a.rows.length);
  a.rows.forEach((x, i) => ['t', 'segTime', 'vel', 'sr', 'dps', 'strokes'].forEach(k => assert.strictEqual(f.rows[i][k], r3(x[k]), f.id + ' row ' + x.d + ' ' + k)));
  assert.strictEqual(f.turns.length, a.turns.length);
  a.turns.forEach((x, i) => {
    const typed = raw.manual.turns[x.wall] && raw.manual.turns[x.wall].turnTime;
    assert.strictEqual(f.turns[i].turnTime, typed ? typed : r3(x.turnTime), f.id + ' turn ' + x.wall);
    ['inOut', 'out15', 'in5', 'pushoff', 'breakout'].forEach(k => assert.strictEqual(f.turns[i][k], r3(x[k]), f.id + ' turn ' + x.wall + ' ' + k));
  });
  assert.strictEqual(f.start.t15, r3(a.start.t15));
  assert.strictEqual(f.start.kicks, raw.manual.kicks !== undefined ? raw.manual.kicks : a.start.kicks, f.id + ' start kicks');
  assert.strictEqual(f.start.breakout, raw.manual.underwaterTime !== undefined ? raw.manual.underwaterTime : r3(a.start.breakout), f.id + ' start breakout');
  assert.strictEqual(f.blocks.length, a.blocks.length);
  assert.strictEqual(f.manual.reaction, 0.65);
});
const f2 = res.races.find(r => r.id === 'dash2');
assert.strictEqual(f2.turns[0].turnTime, 0.81, 'the analyst\'s turn time wins, as in the sheet');
const f3 = res.races.find(r => r.id === 'dash3');
assert.strictEqual(f3.manual.underwaterEstimated, true, 'estimated start distance flagged');
assert.strictEqual(f3.manual.breakoutDist, null);
assert.strictEqual(f3.manual.turns['50'].underwaterEstimated, true, 'estimated turn distance flagged');
const f10 = G.swimDashRaces(['dash10', 'nope', 'tech1']);
same(f10.missing, ['nope', 'tech1'], 'unknown ids and technique analyses are missing');
assert.strictEqual(f10.races[0].start.kicks, 7);
assert.strictEqual(f10.races[0].start.breakout, 4.4);
assert.strictEqual(f10.races[0].turns.length, 7, '400 m: seven turns');

// ---- Μόνο διάβασε
assert.strictEqual(snapshot(), before, 'the dashboard wrote nothing');

// ---- Προσωρινή μνήμη και έλεγχος για αλλαγές
let jsonReads = 0;
const getRange = index.getRange;
index.getRange = function (r, c) { if (c === 15) jsonReads++; return getRange.apply(index, arguments); };
const list1 = G.swimDashList();
assert.strictEqual(jsonReads, 0, 'second list from the cache');
G.swimDashRaces(['dash0', 'dash1']);
assert.strictEqual(jsonReads, 0, 'races from the cache');
let p = G.swimDashPoll(list1.sig);
assert.ok(p.ok && p.changed === false && !p.races, 'no change');
// Νέος αγώνας από το Kinovea
const extra = race(20, { v: 1.78, comp: 'Διασυλλογικοί Αγώνες (δοκιμή)' });
assert.ok(call({ action: 'update', race: extra }).ok);
p = G.swimDashPoll(list1.sig);
assert.ok(p.ok && p.changed && p.races.some(r => r.id === 'dash20') && p.sig !== list1.sig, 'new race seen');
noPrivate(p, 'poll');
// Ενημέρωση ανάλυσης (άλλος τελικός χρόνος): νέα υπογραφή και νέες τιμές, όχι οι παλιές από την προσωρινή μνήμη.
const old0 = G.swimDashRaces(['dash0']).races[0];
const upd = JSON.parse(JSON.stringify(RACES[0]));
upd.checkpoints[upd.checkpoints.length - 1].t = +(upd.checkpoints[upd.checkpoints.length - 1].t + 0.37).toFixed(2);
assert.ok(call({ action: 'update', race: upd }).ok);
const p2 = G.swimDashPoll(p.sig);
assert.ok(p2.changed, 'update seen');
const new0 = G.swimDashRaces(['dash0']).races[0];
assert.strictEqual(r3(new0.summary.finalTime - old0.summary.finalTime), 0.37, 'updated values, not the cached ones');
assert.strictEqual(p2.races.find(r => r.id === 'dash0').finalTime, new0.summary.finalTime);
// Μια γραμμή σβήνεται από το ευρετήριο: φεύγει από τη λίστα και δίνεται ως missing.
const row1 = G.swimIndexRow_(index, 'dash1');
index.deleteRows(row1, 1);
const p3 = G.swimDashPoll(p2.sig);
assert.ok(p3.changed && !p3.races.some(r => r.id === 'dash1'), 'deleted race leaves the list');
same(G.swimDashRaces(['dash1']).missing, ['dash1']);
index.getRange = getRange;

// ---- Το Google διαβάζει ημερομηνίες ως ημερομηνίες: καμία ημερομηνία στις απαντήσεις
const rr = G.swimIndexRow_(index, 'dash4');
index.getRange(rr, 9).setValue(new Date(2026, 9, 17));
index.getRange(rr, 11).setValue(new Date(2026, 9, 30, 16, 45));
res = G.swimDashList();
plain(res, 'list with dates');
assert.ok(res.races.find(r => r.id === 'dash4').updated.length === 16);
plain(G.swimDashRaces(['dash4']), 'race with dates');

// ---- Βοηθητικά
assert.strictEqual(G.swimDashDate_('7.3.2026'), '2026-03-07');
assert.strictEqual(G.swimDashDate_('17/10/2026'), '2026-10-17');
assert.strictEqual(G.swimDashDate_('2026-10-17'), '2026-10-17');
assert.strictEqual(G.swimDashDate_('Οκτώβριος'), '');
assert.strictEqual(G.swimDashUpdated_('30/10/2026 16:45'), '2026-10-30 16:45');
assert.strictEqual(G.swimDashUpdated_(''), '');
assert.notStrictEqual(G.swimDashHash_('a'), G.swimDashHash_('b'));
assert.strictEqual(G.swimDashHash_('abc').length, 8);

// ---- Τρόπος α: Apps Script μέσα στο αρχείο των αναλύσεων (το φύλλο «Αγώνες» στο ίδιο το αρχείο, χωρίς ρυθμίσεις)
{
  const B = context();
  vm.runInContext(webSrc, B.G);
  const own = B.G._ss.insertSheet('Αγώνες');
  Object.keys(index._cells).forEach(k => { own._cells[k] = Object.assign({}, index._cells[k]); });
  delete B.G._props.SWIM_REPORTS_SPREADSHEET_ID;
  const pg = B.G.doGet({ parameter: {} });
  assert.strictEqual(pg.file, 'SwimDashboard', 'doGet of the dashboard project');
  const l = B.G.swimDashList();
  assert.ok(l.ok && l.races.length === RACES.length, 'option a list: ' + (l.races || []).length);
  assert.ok(B.G.swimDashRaces(['dash0']).races[0].summary.finalTime > 0);
  B.who.active = '';
  assert.ok(!B.G.doGet({ parameter: {} }).file, 'option a: anonymous gets text');
}
// Χωρίς ρυθμίσεις και χωρίς φύλλο «Αγώνες»: μήνυμα, όχι νέο αρχείο.
{
  const C = context();
  const files = Object.keys(C.G._all).length;
  const l = C.G.swimDashList();
  assert.ok(!l.ok && l.denied, 'nothing to read: message');
  assert.strictEqual(Object.keys(C.G._all).length, files, 'no spreadsheet created');
}

// ---- Η σελίδα
const scripts = [...html.matchAll(/<script\b([^>]*)>([\s\S]*?)<\/script>/g)];
assert.strictEqual(scripts.length, 1, 'one inline script');
assert.ok(!/\bsrc=/.test(scripts[0][1]), 'no external script');
new vm.Script(scripts[0][2], { filename: 'SwimDashboard.html' });
const calls = [...new Set([...scripts[0][2].matchAll(/call\('(\w+)'/g)].map(m => m[1]))].sort();
same(calls, ['swimDashList', 'swimDashPoll', 'swimDashRaces'], 'server calls: ' + calls);
calls.forEach(fn => assert.strictEqual(typeof G[fn], 'function', fn + ' exists'));
[...html.matchAll(/<link\b[^>]*href="([^"]+)"/g)].forEach(m => assert.ok(/^https:\/\/fonts\.(googleapis|gstatic)\.com(\/|$)/.test(m[1]), 'only Google Fonts: ' + m[1]));
assert.ok(html.indexOf('<?') < 0, 'no template tags');
assert.ok(!/id="demo-data"/.test(html), 'no demo data in the Apps Script page');
assert.ok(Buffer.byteLength(html, 'utf8') < 200 * 1024, 'page size');

console.log('dashboard ok:', RACES.length + 1, 'fictional races, access, read-only, privacy, values, cache, polling, page');
