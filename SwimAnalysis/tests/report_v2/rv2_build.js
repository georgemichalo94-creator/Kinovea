// rv2_build.js (2.22.0): the report of the 2nd version from the script itself (SwimReportV2.gs with SwimRaceAnalysis.gs, as in the
// Apps Script project) on fictional races of every event: with the PB and the reference, with one of them, with none, and with marks
// missing. Every report is built without an error and without NaN / undefined in it; the pages are written as HTML (and, with
// 'pdf', printed with Chromium and checked for overflow).
//   node rv2_build.js <out folder> [keys…] [pdf]
'use strict';
const fs = require('fs');
const path = require('path');
const { makeContext } = require('../mock_gas');
const { all } = require('./rv2_races');

const args = process.argv.slice(2);
const out = args[0] || path.join(__dirname, 'out');
const wantPdf = args.includes('pdf');
const only = args.slice(1).filter(a => a !== 'pdf');
fs.mkdirSync(out, { recursive: true });

const G = makeContext({ athletes: [['unique_id', 'ΟΝΟΜΑΤΕΠΩΝΥΜΟ', 'ΦΥΛΟ'], [990001, 'Αθλητής Δείγμα', 'Ανδρών']] });
const V2 = G.SwimV2;
const BAD = /NaN|undefined|Infinity|\[object|null(?![a-zA-Zά-ώ])/;

function build(p, variant) {
  const race = G.swimNormalizeRace_(JSON.parse(JSON.stringify(p.race)));
  const an = G.swimAnalyze_(race);
  const cmp = {};
  if (variant.pb) {
    const pr = G.swimNormalizeRace_(JSON.parse(JSON.stringify(p.pb)));
    cmp.pb = { race: pr, analysis: G.swimAnalyze_(pr), label: 'PB: ' + G.swimRaceLabel_(pr, G.swimAnalyze_(pr)) };
  }
  if (variant.ref) {
    const er = G.swimReferenceRace_(variant.ref === 'times' ? p.refTimes : p.ref);
    cmp.elite = { race: er, analysis: G.swimAnalyze_(er), label: 'Αναφορά δείγμα' };
  }
  const x = V2.fromScript(race, an, cmp);
  return V2.document(x, variant.short ? { short: 3 } : {});
}

/** A race with marks missing: no strokes in the second half, a checkpoint without its time, no kicks. */
function damaged(p) {
  const q = JSON.parse(JSON.stringify(p));
  const r = q.race;
  const half = r.checkpoints[Math.floor(r.checkpoints.length / 2)].t;
  r.strokes = r.strokes.filter(t => t < half);
  if (r.checkpoints.length > 4) r.checkpoints[2].t = null;
  r.kicks = [];
  r.manual = { reaction: r.manual.reaction, turns: {} };
  return q;
}

const VARIANTS = [
  { name: 'full', pb: true, ref: 'strokes' },
  { name: 'reftimes', pb: true, ref: 'times' },
  { name: 'nopb', pb: false, ref: 'strokes' },
  { name: 'none', pb: false, ref: null },
  { name: 'short', pb: true, ref: 'strokes', short: true },
  { name: 'damaged', pb: true, ref: 'times', damaged: true }
];

const results = [];
let fails = 0;
all().forEach(p => {
  if (only.length && !only.includes(p.ev.key)) return;
  VARIANTS.forEach(v => {
    const key = p.ev.key + '-' + v.name;
    try {
      const doc = build(v.damaged ? damaged(p) : p, v);
      const text = doc.html.replace(/<style>[\s\S]*?<\/style>/, '');
      const bad = text.match(BAD);
      if (bad) {
        fails++;
        const at = text.indexOf(bad[0]);
        console.log('FAIL ' + key + ': "' + bad[0] + '" in …' + text.slice(Math.max(0, at - 120), at + 40).replace(/\s+/g, ' ') + '…');
      }
      fs.writeFileSync(path.join(out, key + '.html'), doc.html);
      results.push({ key, pages: doc.pages, file: key + '.html' });
    } catch (e) {
      fails++;
      console.log('FAIL ' + key + ': ' + (e && e.stack ? e.stack.split('\n').slice(0, 4).join(' | ') : e));
    }
  });
});
console.log('reports: ' + results.length + ', failed: ' + fails);

if (!wantPdf) { process.exit(fails ? 1 : 0); }

(async () => {
  const { chromium } = require('playwright');
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 794, height: 1123 } });
  let over = 0;
  for (const r of results) {
    await page.goto('file://' + path.join(out, r.file));
    await page.evaluate(() => document.fonts.ready);
    const rooms = await page.evaluate(() => Array.from(document.querySelectorAll('.page')).map((pg, k) => {
      const foot = pg.querySelector('.pf');
      const limit = foot ? foot.getBoundingClientRect().top : pg.getBoundingClientRect().bottom;
      let max = 0;
      pg.querySelectorAll('.box, .tiles, .msgs, .gloss, .sec, .cols, .two, .rc, .aim-card, .heros, .ins, .cmpn').forEach(el => { max = Math.max(max, el.getBoundingClientRect().bottom); });
      let wide = 0;
      pg.querySelectorAll('table').forEach(t => { if (t.getBoundingClientRect().right > pg.getBoundingClientRect().right - 30) wide++; });
      return { k: k + 1, room: Math.round(limit - max), wide };
    }));
    const bad = rooms.filter(x => x.room < 0 || x.wide);
    if (bad.length) over++;
    await page.pdf({ path: path.join(out, r.key + '.pdf'), format: 'A4', printBackground: true, preferCSSPageSize: true, margin: { top: 0, right: 0, bottom: 0, left: 0 } });
    console.log((bad.length ? 'OVER ' : 'ok   ') + r.key.padEnd(24) + ' pages ' + rooms.length + '  ' + rooms.map(x => x.k + ':' + x.room + (x.wide ? 'W' : '')).join(' '));
  }
  await browser.close();
  console.log('pdf: ' + results.length + ', over: ' + over);
  process.exit(fails || over ? 1 : 0);
})();
