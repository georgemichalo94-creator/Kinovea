// 2.28.0 (with Kinovea 2.42.0): the relays with their swimmers. Each swimmer of the KOE App gets a row in «Αναλύσεις Αγώνων» with his
// leg (Race ID «<race>#<leg>», event «fs σκέλος» as the legs of the library: not in the PBs of the individual events) and the PDF of
// the team in «Αρχεία Αθλητών/τριων» with his own profile_id (the description stays «ΑΡΧΕΙΟ ΑΝΑΛΥΣΗΣ ΑΓΩΝΑ»); a leg left without an
// athlete of the KOE App loses its row; the coaches of all the swimmers for the package. The report: the names on the legs, the
// exchanges on their own page with the two snapshots of each ({{SNAP:id}}, put in by Kinovea), each swimmer's start against his
// individual best of the event of his leg. A relay of before (no swimmers sent): as before. Fictional teams, names and numbers only.
const assert = require('assert');
const { makeContext } = require('./mock_gas');
const { all } = require('./report_v2/rv2_races');

const ATHLETES = [['unique_id', 'ΟΝΟΜΑΤΕΠΩΝΥΜΟ', 'ΦΥΛΟ', 'ΕΤΟΣ ΓΕΝΝΗΣΗΣ', 'ΚΑΤΗΓΟΡΙΑ', 'ΣΩΜΑΤΕΙΟ', 'EMAIL ΠΡΟΠΟΝΗΤΗ'],
  [990101, 'Πρώτος Δείγμα', 'Ανδρών', 2005, 'Ανδρών', 'Όμιλος Δείγμα', 'coach.a@example.org'],
  [990102, 'Δεύτερος Δείγμα', 'Ανδρών', 2006, 'Ανδρών', 'Όμιλος Δείγμα', 'coach.b@example.org'],
  [990103, 'Τρίτος Δείγμα', 'Ανδρών', 2004, 'Ανδρών', 'Όμιλος Δείγμα', 'Coach.A@example.org']];
const FILES = 'Αρχεία Αθλητών/τριων';
const PREFIX = 'Αρχεία Αθλητών τριων_Files_';
const BAD = /NaN|undefined|Infinity|\[object/;
const near = (a, b, eps) => typeof a === 'number' && Math.abs(a - b) <= (eps || 0.006);

function setup() {
  const G = makeContext({ athletes: ATHLETES });
  G.swimShowConnectionInfo = () => {};
  G.swimSetup();
  const sh = G._ss.insertSheet(FILES);
  sh.getRange(1, 1, 1, 5).setValues([['unique_id', 'profile_id', 'Ημερομηνία εισαγωγής', 'Περιγραφή Αρχείου', 'Αρχείο']]);
  const app = G._drive._root.createFolder('appsheet').createFolder('data').createFolder('KOEApp-1');
  G._filesFolder = app.createFolder(PREFIX);
  G._files = sh;
  const call = body => JSON.parse(G.swimDoPost({ postData: { contents: JSON.stringify(Object.assign({ token: 'tok', app: 'kinovea-swim' }, body)) } }).text);
  return { G, call };
}

const ev = all().find(p => p.ev.key === 'lc-4x100-fs');
const individual = all().find(p => p.ev.key === 'lc-100-fs');
const at = d => (ev.race.checkpoints.find(c => c.d === d) || {}).t;
const snap = c => c.repeat(32);
const LEGS = [
  { leg: 1, style: 'fs', athlete: 'Πρώτος Δείγμα', athleteId: '990101', match: 'code', official: 49.79, officialExchange: 0.66 },
  { leg: 2, style: 'fs', athlete: 'Δεύτερος Δείγμα', athleteId: '990102', match: 'code', official: 50.05, officialExchange: 0.21 },
  { leg: 3, style: 'fs', athlete: 'Τρίτος Δείγμα', athleteId: '990103', match: 'name', official: 49.86, officialExchange: 0.30 },
  { leg: 4, style: 'fs', athlete: 'ΤΕΤΑΡΤΟΣ Δείγμα', athleteId: '', match: '', official: 48.70, officialExchange: 0.18 }];
const EXCHANGES = [1, 2, 3].map(n => ({ n, wall: 100 * n, touch: at(100 * n), feetOff: null, ex: null, official: LEGS[n].officialExchange,
  touchSnap: snap(String.fromCharCode(96 + 2 * n - 1)), offSnap: snap(String.fromCharCode(96 + 2 * n)) }));
const RACE_ID = '0123456789abcdef0123456789abcd01';
const relay = extra => Object.assign({}, ev.race, { id: RACE_ID, athlete: 'Όμιλος Δείγμα', relayLegs: LEGS, relayExchanges: EXCHANGES }, extra || {});

const { G, call } = setup();
const summary = () => {
  const sh = G._ss.getSheetByName(G.SWIM_SUMMARY_SHEET);
  const n = sh.getLastRow() - 1;
  return n > 0 ? sh.getRange(2, 1, n, G.SWIM_SUMMARY_HEADERS.length).getValues() : [];
};
const rowOf = id => summary().find(r => String(r[G.SWIM_SUM.raceId]) === id);

// ---- 1) The analysis of the team: its row as always, and one row for each swimmer of the KOE App, with his leg.
let r = call({ action: 'update', race: relay() });
assert.ok(r.ok, JSON.stringify(r).slice(0, 300));
assert.ok(rowOf(RACE_ID), 'the row of the team as always');
const l1 = rowOf(RACE_ID + '#1'), l2 = rowOf(RACE_ID + '#2'), l3 = rowOf(RACE_ID + '#3');
assert.ok(l1 && l2 && l3, 'a row for each swimmer of the KOE App');
assert.ok(!rowOf(RACE_ID + '#4'), 'none for a swimmer without the KOE App');
assert.strictEqual(l1[0], 990101, 'his registry number (a number, as the other rows)');
assert.strictEqual(l2[1], 'Δεύτερος Δείγμα');
assert.strictEqual(l2[8], 'fs σκέλος', 'the event of a leg, as the legs of the library: ' + l2[8]);
assert.strictEqual(l2[9], '100', 'the distance of the leg');
assert.ok(near(l1[11], at(100)) && near(l2[11], at(200) - at(100)) && near(l3[11], at(300) - at(200)), 'the time of each leg: ' + [l1[11], l2[11], l3[11]].join(' '));
assert.ok(near(l1[17], at(15)) && near(l2[17], at(115) - at(100)), 'the 15: from the signal, from the touch: ' + l1[17] + ' ' + l2[17]);
assert.ok(near(l2[12], 100 / (at(200) - at(100)), 0.01), 'the speed of the leg');
assert.ok(/^\d+\.\d\d \/ \d+\.\d\d$/.test(l2[18]), 'its two 50s: ' + l2[18]);
assert.ok(typeof l2[16] === 'number' && l2[16] > 0, 'its ΔΑΚ: ' + l2[16]);
assert.strictEqual(G.swimIndexEventLabel_(G.swimNormalizeRace_({ style: 'fs', relayLeg: '2' })), 'fs σκέλος');

// ---- 2) The PDF: in the card of each swimmer of the KOE App (the same description), the coaches of all of them.
r = call({ action: 'pdf', race: relay(), coaches: true });
assert.ok(r.ok && r.pdf && r.pdf.url, JSON.stringify(r).slice(0, 300));
assert.ok(r.archive && r.archive.ok && r.archive.legs === 3, 'the PDF in the cards of three swimmers: ' + JSON.stringify(r.archive));
assert.ok(/3 από τους 4 κολυμβητές/.test(r.archive.note), r.archive.note);
let files = G._files.getRange(2, 1, G._files.getLastRow() - 1, 5).getValues();
assert.strictEqual(files.length, 3, 'one row for each swimmer');
assert.deepStrictEqual(files.map(f => f[1]).sort(), [990101, 990102, 990103]);
assert.ok(files.every(f => f[3] === 'ΑΡΧΕΙΟ ΑΝΑΛΥΣΗΣ ΑΓΩΝΑ'), 'the description of the list of the app, as always');
assert.strictEqual(new Set(files.map(f => f[0])).size, 3, 'three ids');
assert.deepStrictEqual(JSON.parse(JSON.stringify(r.coaches)), ['coach.a@example.org', 'coach.b@example.org'], 'the coaches of the swimmers, once each');
assert.ok([RACE_ID, RACE_ID + '#1', RACE_ID + '#2', RACE_ID + '#3'].every(id => rowOf(id)[G.SWIM_SUM.pdf] === r.pdf.url), 'the PDF of the team in every row');
// Again: the same rows.
r = call({ action: 'pdf', race: relay() });
files = G._files.getRange(2, 1, G._files.getLastRow() - 1, 5).getValues();
assert.ok(r.archive.ok && files.length === 3, 'the same rows again: ' + files.length);

// ---- 3) A swimmer who is no more of the KOE App: his row goes (the others stay).
const legs3 = LEGS.map(l => l.leg === 3 ? Object.assign({}, l, { athleteId: '', match: '' }) : l);
r = call({ action: 'update', race: relay({ relayLegs: legs3 }) });
assert.ok(r.ok && !rowOf(RACE_ID + '#3') && rowOf(RACE_ID + '#1') && rowOf(RACE_ID + '#2') && rowOf(RACE_ID), 'the row of leg 3 gone, the others kept');

// ---- 4) The report: the names, the exchanges with their snapshots, the starts against the individual best (leg 2 has one).
const pb = Object.assign({}, individual.race, { id: 'pb-990102-100fs', athlete: 'Δεύτερος Δείγμα', athleteId: '990102', date: '10.11.2026',
  competition: 'Αγώνας ατομικός δείγμα' });
assert.ok(call({ action: 'update', race: pb }).ok, 'the individual best of leg 2 stored');
const pbs = G.swimRelayPbs_(G.swimNormalizeRace_(relay()));
assert.ok(pbs[1] && pbs[1].id === 'pb-990102-100fs' && !pbs[0] && !pbs[2] && !pbs[3], 'the individual best of each swimmer: ' + JSON.stringify(pbs.map(p => p && p.id)));
// A later one does not count; the legs of the library («fs σκέλος») neither.
assert.ok(call({ action: 'update', race: Object.assign({}, pb, { id: 'pb-later', date: '20.12.2026', checkpoints: pb.checkpoints.map(c => ({ d: c.d, t: c.t - 1 })) }) }).ok);
assert.ok(call({ action: 'update', race: Object.assign({}, pb, { id: 'pb-leg', relayLeg: '2', checkpoints: pb.checkpoints.map(c => ({ d: c.d, t: c.t - 2 })) }) }).ok);
assert.strictEqual(G.swimRelayPbs_(G.swimNormalizeRace_(relay()))[1].id, 'pb-990102-100fs', 'not a later one, not a leg');

const v2 = call({ action: 'pdf', v2: true, race: relay() });
assert.ok(v2.ok && v2.v2 && v2.v2.html, JSON.stringify(v2).slice(0, 300));
const html = v2.v2.html, text = html.replace(/<style>[\s\S]*?<\/style>/, '');
assert.ok(!BAD.test(text), 'no NaN / undefined: ' + (text.match(BAD) || [''])[0] + ' … ' + text.substr(Math.max(0, text.search(BAD) - 200), 300));
const titles = h => (h.match(/<div class="ph-r">[^<]*<\/div>/g) || []).map(t => t.replace(/<[^>]+>/g, ''));
assert.ok(titles(html).indexOf('Οι αλλαγές της σκυτάλης') >= 0, 'the page of the exchanges: ' + titles(html).join(' | '));
assert.ok(titles(html).indexOf('Τα σκέλη') < 0 && titles(html).indexOf('Σκέλη και αλλαγές') < 0, 'without the team\'s best or a reference, the legs on the page of the exchanges');
assert.ok(/Πρώτος Δείγμα/.test(text) && /ΤΕΤΑΡΤΟΣ Δείγμα/.test(text) && /<th>ΔΑΚ<\/th><th>Συχνότητα<\/th>/.test(html), 'the names and the values of the legs');
EXCHANGES.forEach(e => {
  assert.ok(html.indexOf('{{SNAP:' + e.touchSnap + '}}') >= 0 && html.indexOf('{{SNAP:' + e.offSnap + '}}') >= 0, 'the two snapshots of exchange ' + e.n);
});
assert.ok(/class="zone z\d"/.test(html) && /Αλλαγή 1 → 2/.test(text) && /επίσημη 0\.21/.test(text), 'an exchange with its zone and its official time');
assert.ok(/Η εκκίνηση στη σκυτάλη/.test(text) && /Η εκκίνηση στη σκυτάλη και στο ατομικό ρεκόρ/.test(text), 'the page of the starts');
assert.ok(/ατομικό ρεκόρ: 100 ελεύθερο <b>\d\d\.\d\d<\/b> · Αγώνας ατομικός δείγμα/.test(html), 'the individual best of leg 2 named');
assert.ok(/(κερδίζει|χάνει) \d+\.\d\d s<\/span> ως τα 15 μ\.: -?\d+\.\d\d από την αλλαγή αντί για αντίδραση/.test(html), 'what leg 2 gains from the exchange');
assert.ok(/Χωρίς ατομικό ρεκόρ στο 100 ελεύθερο στις αναλύσεις του/.test(text) && /δεν είναι από το KOE App/.test(text), 'the legs without a best said');
// The model: the exchange of leg 2 against the reaction of his best, his dive against his best's.
const x = G.SwimV2.fromScript(G.swimNormalizeRace_(relay()), G.swimAnalyze_(G.swimNormalizeRace_(relay())), { relayPbs: G.swimRelayPbs_(G.swimNormalizeRace_(relay())) });
assert.ok(x.relay && x.relay.legs[1].pb && x.relay.legs[1].pb.start.t15 > 5 && x.relay.legs[1].pb.start.block > 0.4, 'his best: its start');
assert.ok(x.race.legs[1].ex > 0 && x.race.legs[1].out15 > x.race.legs[1].ex, 'the exchange and the 15 from the touch');
assert.strictEqual(JSON.stringify(x.details.swimmers), JSON.stringify(LEGS.map(l => l.athlete)));

// With the team's best (an earlier analysis of the team): the page of the legs with their names, against it.
assert.ok(call({ action: 'update', race: Object.assign({}, ev.pb, { id: 'team-best', athlete: 'Όμιλος Δείγμα', date: '1.11.2026' }) }).ok);
const withBest = call({ action: 'pdf', v2: true, race: relay({ compare: { pb: 'team-best' } }) }).v2.html;
assert.ok(titles(withBest).indexOf('Τα σκέλη') >= 0 && titles(withBest).indexOf('Οι αλλαγές της σκυτάλης') >= 0, 'with the team\'s best: the legs and the exchanges: ' + titles(withBest).join(' | '));
assert.ok(/1<\/b> · 0–100 · Πρώτος Δείγμα/.test(withBest), 'the names on the legs');

// ---- 5) A relay of before (no swimmers, no exchanges sent): no rows of swimmers, the pages as before.
const old = Object.assign({}, ev.race, { id: 'fedcba9876543210fedcba9876543210', athlete: 'Όμιλος Δείγμα' });
r = call({ action: 'update', race: old });
assert.ok(r.ok && rowOf(old.id) && !summary().some(row => String(row[G.SWIM_SUM.raceId]).indexOf(old.id + '#') === 0), 'no rows of swimmers');
const oldV2 = call({ action: 'pdf', v2: true, race: old });
assert.ok(oldV2.ok && /Σκέλη και αλλαγές/.test(oldV2.v2.html) && !/Οι αλλαγές της σκυτάλης/.test(oldV2.v2.html) && !/\{\{SNAP:/.test(oldV2.v2.html), 'the pages as before');
assert.strictEqual(G.swimNormalizeRace_(old).relayLegs, undefined, 'the race as before (no new fields)');

// ---- 6) What Kinovea sends, normalized: the legs in their order (bad ones out), the ids of the snapshots safe.
const n = G.swimNormalizeRace_({ relay: true, distance: 400, relayLegs: [{ leg: 3, athlete: 'Γ' }, { leg: 1, athlete: 'Α', athleteId: 990101 }, { leg: 7 }, { leg: 1, athlete: 'διπλός' }, null],
  relayExchanges: [{ n: 2, ex: '0.21', touchSnap: '../x"><script>' }, { n: 1, ex: 0.18, offSnap: snap('f') }] });
assert.deepStrictEqual(JSON.parse(JSON.stringify(n.relayLegs.map(l => [l.leg, l.athlete, l.athleteId]))), [[1, 'Α', '990101'], [3, 'Γ', '']]);
assert.deepStrictEqual(JSON.parse(JSON.stringify(n.relayExchanges.map(e => [e.n, e.ex, e.touchSnap, e.offSnap]))), [[1, 0.18, '', snap('f')], [2, 0.21, '', '']]);

console.log('test_relay OK: rows of the swimmers (' + summary().filter(row => String(row[G.SWIM_SUM.raceId]).indexOf('#') > 0).length + '), PDF in ' +
  files.length + ' cards, exchanges with snapshots, starts against the individual best');
