// Builds the 2nd version of the report templates: one PDF per event, in a folder per pool; checks the free space of every page.
'use strict';
const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');
const { EVENTS, STYLE_NAME } = require('./sim2');
const { document } = require('./pages2');

const OUT = path.join(__dirname, 'out2');
const FOLDER = { 50: '1. Πισίνα 50μ', 25: '2. Πισίνα 25μ', yd: '3. Πισίνα 25 γιάρδες' };
const ORDER = { fs: 0, bk: 1, br: 2, fly: 3, im: 4 };

function fileName(ev, i) {
  const yd = ev.unit === 'yd', u = yd ? ' yd' : 'μ';
  const style = ev.style === 'im' ? (ev.relay ? 'Μικτή' : 'Μικτή ατομική') : STYLE_NAME[ev.style];
  const name = ev.relay ? `4x${ev.distance / 4}${u} ${style} ομαδικό` : `${ev.distance}${u} ${style}`;
  return String(i + 1).padStart(2, '0') + ' - ' + name;
}

(async () => {
  const only = process.argv.slice(2);
  const groups = {};
  EVENTS.forEach(ev => { const f = ev.unit === 'yd' ? FOLDER.yd : FOLDER[ev.pool]; (groups[f] = groups[f] || []).push(ev); });
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 794, height: 1123 } });
  const report = [];
  for (const folder of Object.keys(groups)) {
    const list = groups[folder].slice().sort((a, b) => (a.relay - b.relay) || (a.distance - b.distance) || (ORDER[a.style] - ORDER[b.style]));
    for (let i = 0; i < list.length; i++) {
      const ev = list[i];
      if (only.length && !only.includes(ev.key)) continue;
      const doc = document(ev);
      const dir = path.join(OUT, folder);
      fs.mkdirSync(dir, { recursive: true });
      const base = fileName(ev, i);
      const htmlPath = path.join(OUT, 'html', ev.key + '.html');
      fs.mkdirSync(path.dirname(htmlPath), { recursive: true });
      fs.writeFileSync(htmlPath, doc.html);
      await page.goto('file://' + htmlPath);
      await page.evaluate(() => document.fonts.ready);
      const rooms = await page.evaluate(() => Array.from(document.querySelectorAll('.page')).map((p, k) => {
        const foot = p.querySelector('.pf');
        const limit = foot ? foot.getBoundingClientRect().top : p.getBoundingClientRect().bottom;
        let max = 0;
        p.querySelectorAll('.box, .tiles, .msgs, .gloss, .sec, .cols, .two, .rc, .aim-card, .heros, .ins').forEach(el => { max = Math.max(max, el.getBoundingClientRect().bottom); });
        // Text wider than its table cell or the page.
        let wide = 0;
        p.querySelectorAll('table').forEach(t => { if (t.getBoundingClientRect().right > p.getBoundingClientRect().right - 30) wide++; });
        return { k: k + 1, room: Math.round(limit - max), wide };
      }));
      const pdfPath = path.join(dir, base + '.pdf');
      await page.pdf({ path: pdfPath, format: 'A4', printBackground: true, preferCSSPageSize: true, margin: { top: 0, right: 0, bottom: 0, left: 0 } });
      const bad = rooms.filter(r => r.room < 0 || r.wide);
      report.push({ key: ev.key, file: path.join(folder, base + '.pdf'), pages: rooms.length, bad });
      console.log((bad.length ? 'OVER ' : 'ok   ') + ev.key.padEnd(15) + ' pages ' + rooms.length + '  ' + rooms.map(r => r.k + ':' + r.room + (r.wide ? 'W' : '')).join(' '));
    }
  }
  await browser.close();
  fs.writeFileSync(path.join(OUT, 'report.json'), JSON.stringify(report, null, 1));
  const over = report.filter(r => r.bad.length);
  console.log(over.length ? over.length + ' documents with pages over' : 'all ' + report.length + ' documents fit');
})().catch(e => { console.error(e); process.exit(1); });
