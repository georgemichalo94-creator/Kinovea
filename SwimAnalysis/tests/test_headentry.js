// 2.27.0: the entry of the start is the head's (Kinovea 2.41.0: manual.entryHead, entryDistHead, entryVelHead), as the analyst asked
// («θα βγάλουμε από την εκκίνηση το είσοδος χεριών και θα βάλουμε είσοδος κεφάλι, άλλαξε το από παντού όμως και pdf αναφορές»): the
// sheet, the pages of the PDF and the technique say «κεφάλι» and take the head's values; an analysis of before keeps the hands' and
// says so; the PB, the elite and the previous technique are compared at the entry only with the same kind of entry (else blank / –).
// Fictional races and names only (tests/report_v2/rv2_races.js).
const assert = require('assert');
const { makeContext } = require('./mock_gas');
const { all } = require('./report_v2/rv2_races');

const ATHLETES = [['unique_id', 'ΟΝΟΜΑΤΕΠΩΝΥΜΟ', 'ΦΥΛΟ', 'ΕΤΟΣ ΓΕΝΝΗΣΗΣ', 'ΚΑΤΗΓΟΡΙΑ', 'ΣΩΜΑΤΕΙΟ'], [990001, 'Αθλητής Δείγμα', 'Ανδρών', 2004, 'Ανδρών', 'Όμιλος Δείγμα']];
const BAD = /NaN|undefined|Infinity|\[object/;

function setup() {
  const G = makeContext({ athletes: ATHLETES });
  G.swimShowConnectionInfo = () => {};
  G.swimSetup();
  const call = body => JSON.parse(G.swimDoPost({ postData: { contents: JSON.stringify(Object.assign({ token: 'tok', app: 'kinovea-swim' }, body)) } }).text);
  return { G, call };
}

const clone = o => JSON.parse(JSON.stringify(o));
const p = all().find(x => x.ev.key === 'lc-100-fs');

/** The race of 2.41.0: the entry of the head (its distance and speed), no values of the hands. */
function headRace(src, id, dist) {
  const r = clone(src);
  r.id = id;
  r.manual.entryHead = true;
  r.manual.entryDistHead = dist;
  r.manual.entryVelHead = 4.85;
  delete r.manual.entryDistHands;
  delete r.manual.entryVelHands;
  return r;
}

/** The values of a race's sheet. */
function sheetValues(G, name) {
  const rep = G._all[Object.keys(G._all).find(k => k !== 'DATA')];
  const sh = rep.getSheets().find(s => s.getName() === name);
  assert.ok(sh, 'the sheet ' + name);
  return sh.getRange(1, 1, sh.getLastRow(), Math.max(15, sh.getLastColumn())).getValues();
}
const find = (vals, text) => {
  for (let i = 0; i < vals.length; i++) for (let j = 0; j < vals[i].length; j++) if (vals[i][j] === text) return { r: i, c: j };
  return null;
};
/** The value under a header of the start (the row of the values follows the row of the headers). */
const under = (vals, text) => { const at = find(vals, text); assert.ok(at, 'the header «' + text + '»'); return vals[at.r + 1][at.c]; };
/** The row «PB» of the table «Εκκίνηση, φάσεις και τελικός χρόνος», at the column of the header. */
const pbAt = (vals, text) => {
  const at = find(vals, text);
  assert.ok(at, 'the header «' + text + '»');
  const pb = vals[at.r + 1];
  assert.strictEqual(pb[0], 'PB', 'the row of the PB under the headers');
  return pb[at.c];
};

// 1. The sheet of a race of 2.41.0: the head's titles and values; the PB marked with the hands is not compared at the entry.
{
  const { G, call } = setup();
  const pbHands = clone(p.pb);
  assert.ok(call({ action: 'update', race: pbHands }).ok);
  const race = headRace(p.race, 'head-1', 3.62);
  race.compare = { pb: pbHands.id };
  const r = call({ action: 'update', race });
  assert.ok(r.ok, JSON.stringify(r).slice(0, 300));
  const v = sheetValues(G, r.sheetName);
  assert.strictEqual(Number(under(v, 'Απόσταση εισόδου (κεφάλι) (μ.)')), 3.62, 'the distance of the entry of the head');
  assert.strictEqual(Number(under(v, 'Ταχύτητα εισόδου (κεφάλι) (m/s)')), 4.85, 'the speed of the entry of the head');
  assert.ok(find(v, 'Χρόνος έως την είσοδο του κεφαλιού (sec)'), 'the time of the entry of the head');
  assert.ok(find(v, 'Ταχύτητα: σήμα → είσοδος κεφαλιού (m/s)'), 'the speed signal → entry of the head');
  assert.ok(!find(v, 'Απόσταση εισόδου (Χέρια) (μ.)') && !find(v, 'Ταχύτητα: σήμα → είσοδος χεριών (m/s)'), 'no titles of the hands');
  assert.strictEqual(pbAt(v, 'Είσοδος κεφαλιού (s)'), '', 'the entry of the hands of the PB is not compared with the head\'s');
  assert.strictEqual(pbAt(v, 'Πτήση (s)'), '', 'nor its flight (to the entry)');
  assert.notStrictEqual(pbAt(v, 'Βατήρας (s)'), '', 'the rest of the PB as always');

  // A PB of 2.41.0 too (the head's): compared.
  const pbHead = headRace(p.pb, 'head-pb', 3.55);
  assert.ok(call({ action: 'update', race: pbHead }).ok);
  race.compare = { pb: pbHead.id };
  const r2 = call({ action: 'update', race });
  assert.ok(r2.ok, JSON.stringify(r2).slice(0, 300));
  const v2 = sheetValues(G, r2.sheetName);
  const entry = pbAt(v2, 'Είσοδος κεφαλιού (s)');
  assert.ok(typeof entry === 'number' && entry > 0.5 && entry < 2, 'the entry of the head of the PB: ' + entry);
  assert.ok(typeof pbAt(v2, 'Πτήση (s)') === 'number', 'its flight');

  // 2. The pages of the PDF (2nd version): the head's, the PB of the hands «–» at the entry.
  race.compare = { pb: pbHands.id };
  const pdf = call({ action: 'pdf', v2: true, race: Object.assign({}, race, { reference: p.ref }) });
  assert.ok(pdf.ok && pdf.v2 && pdf.v2.html, JSON.stringify(pdf).slice(0, 300));
  const text = pdf.v2.html.replace(/<style>[\s\S]*?<\/style>/, '');
  assert.ok(!BAD.test(text), 'no NaN / undefined in the pages: ' + (text.match(BAD) || [''])[0]);
  assert.ok(/Απόσταση εισόδου \(κεφάλι\)/.test(text) && /Είσοδος κεφαλιού/.test(text), 'the entry of the head in the pages');
  assert.ok(!/εισόδου \(χέρια\)|είσοδος χεριών|Είσοδος χεριών/.test(text), 'nothing of the hands in the pages');
  assert.ok(/3[.,]62/.test(text), 'the distance of the entry of the head in the pages');

  // The model of the pages: the PB of the hands has no entry (–), the race and its other values as always.
  const N = G.swimNormalizeRace_(race), NP = G.swimNormalizeRace_(pbHands);
  const x = G.SwimV2.fromScript(N, G.swimAnalyze_(N), { pb: { race: NP, analysis: G.swimAnalyze_(NP), label: 'ΑΡ' } });
  assert.strictEqual(x.race.start.entryHead, true);
  assert.strictEqual(x.race.start.entryDist, 3.62);
  assert.strictEqual(x.pb.start.entryHead, false);
  ['entryT', 'flight', 'entryDist', 'entryVel', 'sigEntryVel', 'entryBoVel', 'uwDist'].forEach(k =>
    assert.ok(Number.isNaN(x.pb.start[k]), 'the PB of the hands: no ' + k));
  assert.ok(Number.isFinite(x.pb.start.bd) && Number.isFinite(x.pb.start.t15), 'the breakout and the 15 of the PB as always');
  const NH = G.swimNormalizeRace_(pbHead);
  const y = G.SwimV2.fromScript(N, G.swimAnalyze_(N), { pb: { race: NH, analysis: G.swimAnalyze_(NH), label: 'ΑΡ' } });
  assert.strictEqual(y.pb.start.entryDist, 3.55, 'the PB of the head compared at the entry');
}

// 3. An analysis of before 2.41.0 (the hands'): as it was, and it says so.
{
  const { G, call } = setup();
  const race = clone(p.race);
  race.id = 'hands-1';
  const r = call({ action: 'update', race });
  assert.ok(r.ok, JSON.stringify(r).slice(0, 300));
  const v = sheetValues(G, r.sheetName);
  assert.strictEqual(Number(under(v, 'Απόσταση εισόδου (Χέρια) (μ.)')), p.race.manual.entryDistHands, 'the distance of the entry of the hands');
  assert.ok(find(v, 'Χρόνος έως την είσοδο (sec)') && !find(v, 'Χρόνος έως την είσοδο του κεφαλιού (sec)'), 'the time of the entry as before');
  assert.ok(!find(v, 'Απόσταση εισόδου (κεφάλι) (μ.)'), 'no titles of the head');
  const pdf = call({ action: 'pdf', v2: true, race });
  assert.ok(pdf.ok && pdf.v2 && pdf.v2.html, JSON.stringify(pdf).slice(0, 300));
  const text = pdf.v2.html.replace(/<style>[\s\S]*?<\/style>/, '');
  assert.ok(/Απόσταση εισόδου \(χέρια\)/.test(text), 'the pages of an analysis of before say «χέρια»');
}

// 4. The technique: «Είσοδος κεφαλιού» when the starts are the head's, «Είσοδος χεριών» for an analysis of before; the previous
// technique of the other kind is not compared at the entry.
{
  const { G } = setup();
  const phase = head => Object.assign({ id: 'st1', kind: 'start', wall: 0, attempt: 1, label: 'Εκκίνηση',
    values: { reaction: 0.66, blockTime: 0.74, entryTime: 1.12, entryDist: 3.5, flight: 0.38, t15: 6.3 } }, head ? { entryHead: true } : {});
  const norm = G.swimNormalizeTech_({ phases: [phase(true)] });
  assert.strictEqual(norm.phases[0].entryHead, true, 'the head kept');
  assert.ok(!('entryHead' in G.swimNormalizeTech_({ phases: [phase(false)] }).phases[0]), 'an analysis of before as it was');
  const means = list => G.swimTechMeans_(G.swimTechPhases_({ tech: G.swimNormalizeTech_({ phases: list }) }), 'start');
  assert.strictEqual(means([phase(true)]).entryHead, true);
  assert.strictEqual(means([phase(false)]).entryHead, false);
  const m = G.SWIM_TECH_METRICS.find(x => x.key === 'entryTime');
  assert.strictEqual(G.swimTechLabel_(m, 'μ.', true), 'Είσοδος κεφαλιού (s)');
  assert.strictEqual(G.swimTechLabel_(m, 'μ.', false), 'Είσοδος χεριών (s)');
  const d = G.SWIM_TECH_METRICS.find(x => x.key === 'entryDist');
  assert.strictEqual(G.swimTechLabel_(d, 'μ.', true), 'Απόσταση εισόδου κεφαλιού (μ.)');
}

console.log('HEAD ENTRY OK');
