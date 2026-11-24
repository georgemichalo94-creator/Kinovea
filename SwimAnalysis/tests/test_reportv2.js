// 2.22.0: the report of the 2nd version through the web app, as Kinovea asks for it: 'pdf' with v2 gives the pages (HTML) of the race
// (the sheet as always, no PDF of the sheet), with its PB (a stored analysis) and its reference; 'pdfStore' keeps the PDF that Kinovea
// printed from them in the folder of the reports (in the place of the previous one), in the summary and in «Αρχεία Αθλητών/τριων»;
// the short analysis in its own file; without the file SwimReportV2 in the project, or with pages that fail, the PDF of the sheet as
// before. Fictional races and names only (tests/report_v2/rv2_races.js).
const assert = require('assert');
const { makeContext } = require('./mock_gas');
const { all } = require('./report_v2/rv2_races');

const ATHLETES = [['unique_id', 'ΟΝΟΜΑΤΕΠΩΝΥΜΟ', 'ΦΥΛΟ', 'ΕΤΟΣ ΓΕΝΝΗΣΗΣ', 'ΚΑΤΗΓΟΡΙΑ', 'ΣΩΜΑΤΕΙΟ'], [990001, 'Αθλητής Δείγμα', 'Ανδρών', 2004, 'Ανδρών', 'Όμιλος Δείγμα']];
const BAD = /NaN|undefined|Infinity|\[object/;
const fakePdf = tag => Buffer.concat([Buffer.from('%PDF-1.7\n'), Buffer.alloc(3000, 32), Buffer.from('\n% ' + tag + '\n%%EOF\n')]).toString('base64');

function setup() {
  const G = makeContext({ athletes: ATHLETES });
  G.swimShowConnectionInfo = () => {};
  G.swimSetup();
  const call = body => JSON.parse(G.swimDoPost({ postData: { contents: JSON.stringify(Object.assign({ token: 'tok', app: 'kinovea-swim' }, body)) } }).text);
  return { G, call };
}

const events = all();
const pick = key => events.find(p => p.ev.key === key);

// 1. A 100 free: the PB stored first, then the PDF of the 2nd version with the PB and the reference.
{
  const { G, call } = setup();
  const p = pick('lc-100-fs');
  const pbStored = call({ action: 'update', race: p.pb });
  assert.ok(pbStored.ok, JSON.stringify(pbStored).slice(0, 300));
  const race = Object.assign({}, p.race, { compare: { pb: p.pb.id }, reference: p.ref });
  const r = call({ action: 'pdf', v2: true, race });
  assert.ok(r.ok, JSON.stringify(r).slice(0, 300));
  assert.strictEqual(r.version, '2.23.0');
  assert.ok(r.v2 && typeof r.v2.html === 'string' && r.v2.html.length > 50000, 'the pages of the 2nd version');
  assert.ok(!r.pdf, 'no PDF of the sheet with v2');
  assert.ok(!r.needCharts, 'no charts to draw for the pages of the 2nd version');
  assert.ok(r.v2.pages >= 6 && r.v2.pages <= 12, 'pages ' + r.v2.pages);
  const text = r.v2.html.replace(/<style>[\s\S]*?<\/style>/, '');
  assert.ok(!BAD.test(text), 'no NaN / undefined in the pages: ' + (text.match(BAD) || [''])[0]);
  assert.ok(/Αθλητής Δείγμα/.test(text) && /Αγώνας δείγμα/.test(text), 'the names of the race');
  assert.ok(/48\.9\d/.test(text), 'the final time of the race');
  assert.ok(/ΑΡ/.test(text) && /Αναφορά δείγμα/.test(text), 'the PB and the reference named');
  // The sheet of the race was made (the report of the KOE App as always).
  assert.ok(r.sheetName && r.sheetUrl, 'the sheet of the race');

  // 2. The PDF printed by Kinovea goes in the folder of the reports, in the summary and in «Αρχεία Αθλητών/τριων».
  const stored = call({ action: 'pdfStore', race, raceId: race.id, pdf: fakePdf('v2'), pdfData: true });
  assert.ok(stored.ok, JSON.stringify(stored).slice(0, 300));
  assert.ok(stored.pdf && stored.pdf.url && stored.pdf.v2, 'the stored PDF');
  assert.strictEqual(stored.pdf.name, r.sheetName + '.pdf', 'named as the PDF of the sheet');
  assert.ok(stored.archive && stored.archive.ok !== undefined, 'the archive answered');
  assert.ok(stored.pdf.data && stored.pdf.data.length > 4000, 'the PDF itself for the package');
  const folder = G._drive.getFoldersByName(G.SWIM_REPORT_FOLDER).next();
  const files = [];
  const it = folder.getFilesByName(r.sheetName + '.pdf');
  while (it.hasNext()) { const f = it.next(); if (!f.isTrashed || !f.isTrashed()) files.push(f); }
  assert.ok(files.length >= 1, 'the PDF in the folder of the reports');
  // Again: in the place of the previous one.
  const again = call({ action: 'pdfStore', race, raceId: race.id, pdf: fakePdf('v2b') });
  assert.ok(again.ok && again.pdf.url, 'stored again');

  // 3. Not a PDF: refused, the previous one stays.
  const bad = call({ action: 'pdfStore', race, raceId: race.id, pdf: Buffer.from('<html>not a pdf</html>').toString('base64') });
  assert.ok(!bad.ok && /έγκυρο/.test(bad.error || ''), 'not a PDF: ' + JSON.stringify(bad).slice(0, 200));

  // 4. A race that was never sent: no report to put it in.
  const none = call({ action: 'pdfStore', race: Object.assign({}, race, { id: 'never-sent' }), raceId: 'never-sent', pdf: fakePdf('x') });
  assert.ok(!none.ok && /Αποστολή τώρα/.test(none.error || ''), 'no report: ' + JSON.stringify(none).slice(0, 200));
}

// 5. The short analysis in its own file (two files): its pages and its PDF.
{
  const { call } = setup();
  const p = pick('lc-200-fs');
  const race = Object.assign({}, p.race, { reference: p.ref });
  const r = call({ action: 'pdf', v2: true, race, pdfParts: { short: true, full: true, separate: true } });
  assert.ok(r.ok && r.v2 && r.v2.htmlShort && r.v2.pagesShort === 3, 'the pages of the short analysis: ' + JSON.stringify(r.v2 ? { p: r.v2.pages, s: r.v2.pagesShort } : r).slice(0, 200));
  assert.ok(r.v2.pages > 3, 'the full analysis');
  const stored = call({ action: 'pdfStore', race, raceId: race.id, pdf: fakePdf('full'), pdfShort: fakePdf('short') });
  assert.ok(stored.ok && stored.pdfShort && / - Σύντομη ανάλυση\.pdf$/.test(stored.pdfShort.name), 'the short analysis in its own file');
  // Only the short analysis: the three pages.
  const onlyShort = call({ action: 'pdf', v2: true, race, pdfParts: { short: true, full: false } });
  assert.ok(onlyShort.ok && onlyShort.v2 && onlyShort.v2.pages === 3 && !onlyShort.v2.htmlShort, 'only the short analysis');
}

// 6. Every event once (the relays, the medley, 25 m and yards): the pages without an error, without the PB and the reference too.
{
  const { call } = setup();
  let n = 0;
  events.forEach((p, i) => {
    const race = Object.assign({}, p.race, i % 3 === 0 ? {} : { reference: i % 3 === 1 ? p.ref : p.refTimes });
    const r = call({ action: 'pdf', v2: true, race });
    assert.ok(r.ok && r.v2 && r.v2.html, p.ev.key + ': ' + JSON.stringify(r).slice(0, 300));
    const text = r.v2.html.replace(/<style>[\s\S]*?<\/style>/, '');
    assert.ok(!BAD.test(text), p.ev.key + ': ' + (text.match(BAD) || [''])[0] + ' in the pages');
    n++;
  });
  assert.strictEqual(n, events.length);
}

// 7. Without the file SwimReportV2 in the project (the script of the KOE App without it): the PDF of the sheet, as before.
{
  const { G, call } = setup();
  const p = pick('lc-100-fs');
  G.SwimV2 = undefined;   // (a 'var' of the project: the file is not there)
  const r = call({ action: 'pdf', v2: true, race: p.race });
  assert.ok(r.ok && !r.v2 && r.pdf && r.pdf.url, 'the PDF of the sheet: ' + JSON.stringify(r).slice(0, 300));
  assert.strictEqual(r.v2Missing, true, 'Kinovea is told the file is missing');
  // An update (not a PDF) says nothing of it.
  const u = call({ action: 'update', v2: true, race: p.race });
  assert.ok(u.ok && u.v2Missing === undefined, 'nothing said for an update');
}

// 8. Pages that fail: the PDF of the sheet, and why.
{
  const { G, call } = setup();
  const p = pick('lc-100-fs');
  G.SwimV2.fromScript = () => { throw new Error('δοκιμή'); };
  const r = call({ action: 'pdf', v2: true, race: p.race });
  assert.ok(r.ok && !r.v2 && r.pdf && r.pdf.url && /δοκιμή/.test(r.v2Error || ''), 'the PDF of the sheet after an error: ' + JSON.stringify(r).slice(0, 300));
}

// 9. A training video: no pages of the race report (the technique report as before).
{
  const { call } = setup();
  const p = pick('lc-100-fs');
  const r = call({ action: 'pdf', v2: true, race: Object.assign({}, p.race, { kind: 'training', checkpoints: [] }) });
  assert.ok(r.ok && !r.v2, 'no race pages for a training video');
}

// 10. (2.23.0) «Πού κρύβεται ο χρόνος» in the place of the target time: no target time anywhere; the map of the race with the time
// of every recommendation in its zones (the same total, nothing outside the course); the pacing with today's time; the ΔΑΚ in capitals.
{
  const G = makeContext({ athletes: ATHLETES });
  const V2 = G.SwimV2;
  const GREEK = 'Α-Ωα-ωάέήίόύώϊϋΐΰΆΈΉΊΌΎΏ';
  const lowDak = new RegExp('(^|[^' + GREEK + '])δακ([^' + GREEK + ']|$)');
  let maps = 0, paces = 0;
  events.forEach(p => {
    const race = G.swimNormalizeRace_(JSON.parse(JSON.stringify(p.race)));
    const pr = G.swimNormalizeRace_(JSON.parse(JSON.stringify(p.pb)));
    const er = G.swimReferenceRace_(p.ref);
    const cmp = { pb: { race: pr, analysis: G.swimAnalyze_(pr), label: 'PB' }, elite: { race: er, analysis: G.swimAnalyze_(er), label: 'Αναφορά δείγμα' } };
    const x = V2.fromScript(race, G.swimAnalyze_(race), cmp);
    const R = V2.recommendations(x);
    const doc = V2.document(x, {});
    const text = doc.html.replace(/<style>[\s\S]*?<\/style>/, '');
    const key = p.ev.key;
    assert.ok(!/Στόχος για τον επόμενο αγώνα|Στόχος με τις προτάσεις|Χρόνοι-στόχοι|ο στόχος είναι/.test(text), key + ': no target time');
    assert.ok(!lowDak.test(text.replace(/<[^>]+>/g, ' ')), key + ': the ΔΑΚ in capitals');
    const withGain = R.recs.filter(r => r.gain !== null && r.gain > 0);
    const total = withGain.reduce((s0, r) => s0 + r.gain, 0);
    assert.ok(Math.abs(R.total - total) < 1e-9, key + ': the total of the gains');
    const inZones = R.map.zones.reduce((s0, z) => s0 + z.gain, 0);
    const mapped = withGain.filter(r => r.zones && r.zones.some(z => z.w > 0)).reduce((s0, r) => s0 + r.gain, 0);
    assert.ok(Math.abs(inZones - mapped) < 1e-6, key + ': every gain in its zones ' + inZones + ' / ' + mapped);
    R.map.zones.forEach(z => assert.ok(z.from >= 0 && z.to <= R.map.D && z.to > z.from && z.gain > 0, key + ': a zone on the course ' + JSON.stringify(z)));
    if (withGain.length) {
      maps++;
      assert.ok(/Πού κρύβεται ο χρόνος/.test(text) && /class="rmap big"/.test(text) && /class="rmap"/.test(text), key + ': the map on pages 1 and 2');
      assert.ok(/Η μεγαλύτερη ευκαιρία/.test(text), key + ': the biggest opportunity');
    }
    if (R.splits) {
      paces++;
      const sum = R.splits.reduce((s0, t) => s0 + t, 0);
      assert.ok(Math.abs(sum - x.race.final) < 1e-6, key + ': the same time ' + sum + ' / ' + x.race.final);
      if (!x.ev.relay) assert.ok(/Ίδιος χρόνος, καλύτερη κατανομή/.test(text), key + ': the pacing page');
    }
  });
  assert.ok(maps >= 40 && paces >= 20, 'maps ' + maps + ', pacing ' + paces);
}

console.log('test_reportv2: ok');
