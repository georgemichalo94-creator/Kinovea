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
  assert.strictEqual(r.version, '2.26.1');
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
  // (2.25.0) Four: the race at a glance, the phases against the reference, the conclusion, the ΔΑΚ (until 2.24.1 three, without the conclusion).
  assert.ok(r.ok && r.v2 && r.v2.htmlShort && r.v2.pagesShort === 4, 'the pages of the short analysis: ' + JSON.stringify(r.v2 ? { p: r.v2.pages, s: r.v2.pagesShort } : r).slice(0, 200));
  assert.ok(r.v2.pages > 4, 'the full analysis');
  assert.ok(/Τι έδειξε ο αγώνας/.test(r.v2.htmlShort) && /Τι δείχνει η σύγκριση ανά φάση/.test(r.v2.htmlShort), 'the short analysis has the phases and the conclusion');
  const stored = call({ action: 'pdfStore', race, raceId: race.id, pdf: fakePdf('full'), pdfShort: fakePdf('short') });
  assert.ok(stored.ok && stored.pdfShort && / - Σύντομη ανάλυση\.pdf$/.test(stored.pdfShort.name), 'the short analysis in its own file');
  // Only the short analysis: its four pages.
  const onlyShort = call({ action: 'pdf', v2: true, race, pdfParts: { short: true, full: false } });
  assert.ok(onlyShort.ok && onlyShort.v2 && onlyShort.v2.pages === 4 && !onlyShort.v2.htmlShort, 'only the short analysis');
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

// 10. (2.23.0, 2.24.0) No target time anywhere; the map of the race with the whole time each phase loses in its zones (the same total,
// nothing outside the course, not the ΔΑΚ of the second half); the pacing with today's time; the ΔΑΚ in capitals.
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
    const lost = R.recs.filter(r => r.lost !== null && r.lost > 0 && r.key !== 'dak');
    const inZones = R.map.zones.reduce((s0, z) => s0 + z.lost, 0);
    const mapped = lost.filter(r => r.zones && r.zones.some(z => z.w > 0)).reduce((s0, r) => s0 + r.lost, 0);
    assert.ok(Math.abs(inZones - mapped) < 1e-6, key + ': every time lost in its zones ' + inZones + ' / ' + mapped);
    R.map.zones.forEach(z => assert.ok(z.from >= 0 && z.to <= R.map.D && z.to > z.from && z.lost > 0 && z.key !== 'dak', key + ': a zone on the course ' + JSON.stringify(z)));
    if (R.map.zones.length) {
      maps++;
      assert.ok(/Πού χάνεται ο χρόνος/.test(text) && /class="rmap big"/.test(text), key + ': the map on page 2');
    }
    if (R.splits) {
      paces++;
      const sum = R.splits.reduce((s0, t) => s0 + t, 0);
      assert.ok(Math.abs(sum - x.race.final) < 1e-6, key + ': the same time ' + sum + ' / ' + x.race.final);
      if (!x.ev.relay) assert.ok(/Ο ίδιος χρόνος με την κατανομή (της αναφοράς|του )/.test(text) && !/καλύτερη κατανομή|όπου χρειάζεται/.test(text), key + ': the pacing page, described');
    }
  });
  assert.ok(maps >= 40 && paces >= 20, 'maps ' + maps + ', pacing ' + paces);
}

// 11. (2.24.0) How far behind the reference each phase is, in %: the level of each phase from its own times (100 × lost / time, the
// reference first, else the PB); the recommendations in the order of the %, then the others; no gain of half the way, no target of
// half the way (the takeovers and the pacing keep theirs); the weakest phase on page 1 and in the third message; the bars on page 2.
{
  const G = makeContext({ athletes: ATHLETES });
  const V2 = G.SwimV2;
  let weakCards = 0, checkedLevels = 0;
  events.forEach(p => {
    const race = G.swimNormalizeRace_(JSON.parse(JSON.stringify(p.race)));
    const pr = G.swimNormalizeRace_(JSON.parse(JSON.stringify(p.pb)));
    const er = G.swimReferenceRace_(p.ref);
    const cmp = { pb: { race: pr, analysis: G.swimAnalyze_(pr), label: 'PB' }, elite: { race: er, analysis: G.swimAnalyze_(er), label: 'Αναφορά δείγμα' } };
    const x = V2.fromScript(race, G.swimAnalyze_(race), cmp);
    const R = V2.recommendations(x);
    const doc = V2.document(x, {});
    const text = doc.html.replace(/<style>[\s\S]*?<\/style>/, '');
    const plain = text.replace(/<[^>]+>/g, ' ');
    const key = p.ev.key;
    const fin = v => typeof v === 'number' && isFinite(v);
    // The start and the finish from their own times.
    const own = (k, get) => {
      const l = R.levels.find(q => q.key === k);
      const c = fin(get(x.ref)) ? x.ref : fin(get(x.pb)) ? x.pb : null;
      if (!c || !fin(get(x.race))) { assert.ok(!l, key + ': no level of ' + k + ' without a comparison'); return; }
      const want = 100 * (get(x.race) - get(c)) / get(x.race);
      assert.ok(l && Math.abs(l.pct - want) < 1e-9 && Math.abs(l.lost - (get(x.race) - get(c))) < 1e-9 && l.ref === (c === x.ref), key + ': the level of ' + k + ' ' + (l ? l.pct : '-') + ' / ' + want);
      checkedLevels++;
    };
    own('start', r => r.start.t15);
    own('finish', r => r.finish.last5);
    R.levels.forEach(l => assert.ok(fin(l.pct) && fin(l.lost) && ['start', 'swim', 'turn', 'finish'].includes(l.key), key + ': a level ' + JSON.stringify(l)));
    // The order: with a % first, by the %; then by the time lost.
    let seenNoPct = false, prev = Infinity;
    R.recs.forEach(r => {
      assert.ok(!('gain' in r), key + ': no gain of half the way in ' + r.key);
      if (r.pct === null) { seenNoPct = true; return; }
      assert.ok(!seenNoPct && r.pct <= prev + 1e-12 && r.lost > 0, key + ': the order of the % at ' + r.key);
      prev = r.pct;
    });
    // (2.25.0) No action and no target: what the comparison means, and a short phrase for the conclusion.
    R.recs.forEach(r => assert.ok(!('target' in r) && !('action' in r) && typeof r.meaning === 'string' && r.meaning.length > 20 && typeof r.why === 'string' && r.why.length > 3, key + ': a description in ' + r.key));
    assert.ok(!/μέση της απόστασης|μισή απόσταση|περιθωρ|κερδίζεται|Με σειρά κέρδους/.test(plain), key + ': none of the words of the gains');
    const behind = R.levels.filter(l => l.pct > 0.05);
    if (behind.length) {
      weakCards++;
      const weak = behind.slice().sort((p0, q0) => q0.pct - p0.pct)[0];
      const pctText = '−' + weak.pct.toFixed(1).replace('.', ',') + '%';
      assert.ok(plain.includes('Η πιο αδύνατη φάση') && plain.includes(pctText), key + ': the weakest phase on page 1 ' + pctText);
      assert.ok(/Υστέρηση ανά φάση/.test(text) && (text.match(/class="acb"/g) || []).length === R.levels.length, key + ': the bars of page 2');
      assert.ok(/χάνει \d+\.\d\d s/.test(plain), key + ': the time lost on the cards');
    }
  });
  assert.ok(weakCards >= 40 && checkedLevels >= 80, 'weakest phases ' + weakCards + ', levels checked ' + checkedLevels);

  // The race against itself as its reference: every phase at 0%, no phase behind, no recommendation of a phase, the card of the swimming.
  const p = pick('lc-200-fs');
  const race = G.swimNormalizeRace_(JSON.parse(JSON.stringify(p.race)));
  const same = { race: G.swimNormalizeRace_(JSON.parse(JSON.stringify(p.race))), label: 'Ίδιος' };
  same.analysis = G.swimAnalyze_(same.race);
  const x = V2.fromScript(race, G.swimAnalyze_(race), { pb: same, elite: same });
  const R = V2.recommendations(x);
  assert.ok(R.levels.length >= 3 && R.levels.every(l => Math.abs(l.pct) < 1e-9), 'the same race: every phase at 0% ' + JSON.stringify(R.levels));
  assert.ok(!R.recs.some(r => ['start', 'turn', 'swim', 'finish'].includes(r.key)), 'the same race: no recommendation of a phase');
  const doc = V2.document(x, {});
  assert.ok(!/Η πιο αδύνατη φάση/.test(doc.html), 'the same race: no weakest phase');
}

// 12. (2.24.1) The last metres against the PB and the reference only at the same distance from the finish: a final without a time 15
// before the finish (its analysis: 15, 25, 5 before each wall, the wall, 15 and 25 after it, 5 before the finish) gave its last 25
// against the race's last 15 (−4.41 s, in blue). Now nothing there («–»); a reference with the time there: its own last 15.
{
  const G = makeContext({ athletes: ATHLETES });
  const V2 = G.SwimV2;
  const fin = v => typeof v === 'number' && isFinite(v);
  const model = (p, refTimes) => {
    const race = G.swimNormalizeRace_(JSON.parse(JSON.stringify(p.race)));
    const pr = G.swimNormalizeRace_(JSON.parse(JSON.stringify(p.pb)));
    const er = G.swimReferenceRace_(Object.assign({}, p.ref, { times: refTimes }));
    const cmp = { pb: { race: pr, analysis: G.swimAnalyze_(pr), label: 'PB' }, elite: { race: er, analysis: G.swimAnalyze_(er), label: 'Αναφορά δείγμα' } };
    return V2.fromScript(race, G.swimAnalyze_(race), cmp);
  };
  // The points of the analysis of a final.
  const finalPoints = ev => {
    const D0 = ev.distance, P = ev.pool, keep = new Set([15, 25, D0 - 5, D0]);
    for (let w = P; w < D0; w += P) [w - 5, w, w + 15, w + 25].forEach(d => keep.add(d));
    return keep;
  };
  const p = pick('lc-100-fs');
  const D = p.ev.distance;
  assert.ok(!finalPoints(p.ev).has(D - 15) && p.race.checkpoints.some(c => c.d === D - 15), 'the race has its 85, the final not');
  const x = model(p, p.ref.times.filter(q => finalPoints(p.ev).has(q.d)));
  assert.strictEqual(x.race.finish.lastX, 15);
  assert.strictEqual(x.ref.finish.lastX, 15, 'the reference at the distance of the race');
  assert.ok(!fin(x.ref.finish.lastXt), 'no last 15 of a final without its 85: ' + x.ref.finish.lastXt);
  const html = V2.document(x, {}).html;
  const at = html.indexOf('>Τελευταία 15');
  const card = at >= 0 ? html.slice(at, html.indexOf('</div></div>', html.indexOf('class="t-d"', at)) + 12) : '';
  assert.ok(/Αναφ\. –<\/span>/.test(card) && !/Αναφ\. <span/.test(card), 'the card of the last 15 without the final: ' + card);
  // With its 85: its own last 15.
  const y = model(p, p.ref.times);
  const tRef = d => p.ref.times.find(q => q.d === d).t;
  assert.ok(Math.abs(y.ref.finish.lastXt - (tRef(D) - tRef(D - 15))) < 1e-6, 'the last 15 of the reference: ' + y.ref.finish.lastXt);
  // Every event, with all the points of the reference and with those of a final: the last metres of the PB and of the reference only at
  // the distance of the race, from their own times there, and always when they have them.
  let compared = 0, missing = 0;
  events.forEach(q => {
    const Dq = q.ev.distance;
    [q.ref.times, q.ref.times.filter(c => finalPoints(q.ev).has(c.d))].forEach(times => {
      const m = model(q, times);
      const lx = m.race.finish.lastX;
      [[m.pb.finish, q.pb.checkpoints, 'PB'], [m.ref.finish, times, 'reference']].forEach(([f, own, who]) => {
        assert.ok(f.lastX === lx || (!fin(f.lastX) && !fin(lx)), q.ev.key + ': the ' + who + ' at the same distance ' + f.lastX + ' / ' + lx);
        const t = d => { const c = own.find(c0 => c0.d === d); return c ? c.t : NaN; };
        const want = fin(lx) ? t(Dq) - t(Dq - lx) : NaN;
        if (fin(want)) {
          assert.ok(Math.abs(f.lastXt - want) < 1e-6, q.ev.key + ': the last ' + lx + ' of the ' + who + ' ' + f.lastXt + ' / ' + want);
          compared++;
        } else {
          assert.ok(!fin(f.lastXt), q.ev.key + ': no last ' + lx + ' of the ' + who + ' without its times there: ' + f.lastXt);
          missing++;
        }
      });
    });
  });
  assert.ok(compared >= 60 && missing >= 10, 'compared ' + compared + ', without ' + missing);
}

// 13. (2.25.0) What the comparison means, not what to change (the analyst's request): no proposal, target, training set or exercise in
// the pages; every card of page 2 with «Τι σημαίνει»; the conclusion page after it, with the phases that make up the whole race against
// the reference (their differences are the whole gap when all of them have it) and the findings; without a PB and a reference the race
// against itself.
{
  const G = makeContext({ athletes: ATHLETES });
  const V2 = G.SwimV2;
  const PRESCRIBE = /Πρόταση|Στόχος|με tempo trainer|σετ |Προπόνηση|προπόνησ|επαναλήψεις|Πιο γρήγορη|Μεγαλύτερη υποβρύχια|Κράτημα/;
  let conclusions = 0, partitions = 0, summaries = 0;
  events.forEach(p => {
    const race = G.swimNormalizeRace_(JSON.parse(JSON.stringify(p.race)));
    const pr = G.swimNormalizeRace_(JSON.parse(JSON.stringify(p.pb)));
    const er = G.swimReferenceRace_(p.ref);
    const cmp = { pb: { race: pr, analysis: G.swimAnalyze_(pr), label: 'PB' }, elite: { race: er, analysis: G.swimAnalyze_(er), label: 'Αναφορά δείγμα' } };
    const x = V2.fromScript(race, G.swimAnalyze_(race), cmp);
    const doc = V2.document(x, {});
    const text = doc.html.replace(/<style>[\s\S]*?<\/style>/, '');
    const plain = text.replace(/<[^>]+>/g, ' ');
    const key = p.ev.key;
    assert.ok(!PRESCRIBE.test(plain), key + ': nothing prescribed: ' + (plain.match(PRESCRIBE) || [''])[0]);
    const cards = Math.min(5, doc.R.recs.length);
    assert.strictEqual((text.match(/<div class="rc-l">Τι σημαίνει<\/div>/g) || []).length, cards, key + ': every card with what it means');
    assert.ok(/Τι έδειξε ο αγώνας/.test(text), key + ': the conclusion');
    conclusions++;
    // The pages in order: the conclusion right after the phases.
    const at = t0 => text.indexOf(t0);
    if (cards) assert.ok(at('Τι δείχνει η σύγκριση ανά φάση') < at('Τι έδειξε ο αγώνας'), key + ': the conclusion after the phases');
    // The phases make up the whole race: their differences from the reference are the whole gap.
    const parts = ['start', 'swim', 'turn', 'exchange', 'finish'].filter(k => typeof x.race.composition[k] === 'number' && x.race.composition[k] > 0);
    const fin = v => typeof v === 'number' && isFinite(v);
    if (parts.every(k => fin(x.race.composition[k]) && fin(x.ref.composition[k])) && fin(x.race.final) && fin(x.ref.final)) {
      const sum = parts.reduce((s0, k) => s0 + x.race.composition[k] - x.ref.composition[k], 0);
      assert.ok(Math.abs(sum - (x.race.final - x.ref.final)) < 0.011, key + ': the phases are the whole gap ' + sum + ' / ' + (x.race.final - x.ref.final));
      assert.ok(/Πού βρίσκεται η διαφορά/.test(text), key + ': the table of the phases');
      partitions++;
      if (parts.some(k => x.race.composition[k] - x.ref.composition[k] > 0.05)) {
        assert.ok(/Συνοπτικά/.test(text) && /class="msg"/.test(text.slice(at('Τι έδειξε ο αγώνας'))), key + ': the findings');
        summaries++;
      }
    }
  });
  assert.ok(conclusions === events.length && partitions >= 30 && summaries >= 30, 'conclusions ' + conclusions + ', partitions ' + partitions + ', findings ' + summaries);

  // Without a PB and a reference: the race against itself, no table of the phases.
  const p = pick('lc-200-fs');
  const race = G.swimNormalizeRace_(JSON.parse(JSON.stringify(p.race)));
  const x = V2.fromScript(race, G.swimAnalyze_(race), {});
  const text = V2.document(x, {}).html;
  assert.ok(/Τι έδειξε ο αγώνας/.test(text) && /Χωρίς ΑΡ και αναφορά/.test(text) && !/Πού βρίσκεται η διαφορά/.test(text), 'the conclusion without a comparison');
}

console.log('test_reportv2: ok');
