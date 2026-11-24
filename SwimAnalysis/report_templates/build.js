// Builds the report templates: HTML → PDF (Chromium), and page previews (PNG) to check them.
'use strict';
const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');
const { document } = require('./pages');

const OUT = path.join(__dirname, 'out');
const NAMES = {
  '50': '1. Πρότυπο αναφοράς 50 ελεύθερο',
  '100': '2. Πρότυπο αναφοράς 100 ελεύθερο',
  '200': '3. Πρότυπο αναφοράς 200 ελεύθερο',
  '400': '4. Πρότυπο αναφοράς 400 ελεύθερο',
  '400im': '5. Πρότυπο αναφοράς 400 μικτή',
  '800': '6. Πρότυπο αναφοράς 800 ελεύθερο',
  '1500': '7. Πρότυπο αναφοράς 1500 ελεύθερο'
};

(async () => {
  fs.mkdirSync(OUT, { recursive: true });
  const keys = process.argv.slice(2).length ? process.argv.slice(2) : Object.keys(NAMES);
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 794, height: 1123 } });
  for (const key of keys) {
    const html = document(key);
    const htmlPath = path.join(OUT, key + '.html');
    fs.writeFileSync(htmlPath, html);
    await page.goto('file://' + htmlPath);
    await page.evaluate(() => document.fonts.ready);
    // Overflow check: content that goes past the footer of its page.
    const over = await page.evaluate(() => Array.from(document.querySelectorAll('.page')).map((p, i) => {
      const foot = p.querySelector('.pf');
      const limit = foot ? foot.getBoundingClientRect().top : p.getBoundingClientRect().bottom;
      let max = 0;
      p.querySelectorAll('.box, .tiles, .finds, .gloss, .sec, .cols, .two').forEach(el => { max = Math.max(max, el.getBoundingClientRect().bottom); });
      return { page: i + 1, room: Math.round(limit - max) };
    }));
    const pdfPath = path.join(OUT, NAMES[key] + '.pdf');
    await page.pdf({ path: pdfPath, format: 'A4', printBackground: true, preferCSSPageSize: true, margin: { top: 0, right: 0, bottom: 0, left: 0 } });
    console.log(key, 'pages', over.length, 'room(px):', over.map(o => o.page + ':' + o.room).join(' '));
  }
  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
