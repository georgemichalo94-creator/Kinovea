// The app on an emulated phone, from the video to the report, on the synthetic clips of gen_clips.sh (every frame of
// them carries its number, their sound a start signal at a known time). Fictional athlete names only.
// Usage: node e2e.js <folder of the clips> <folder for the screenshots>
'use strict';
const fs = require('fs'), path = require('path'), http = require('http'), { execFileSync } = require('child_process');
const { chromium } = require('playwright');

const clips = process.argv[2], shots = process.argv[3];
if (!clips || !shots) { console.error('usage: node e2e.js <clips> <screenshots>'); process.exit(2); }
fs.mkdirSync(shots, { recursive: true });
let failed = 0;
function check(name, ok, detail) { console.log((ok ? 'ok   ' : 'FAIL ') + name + (detail ? '  ' + detail : '')); if (!ok) failed++; }

// The page as it is published: the file inside the skeleton that the platform puts around it.
const SKELETON = '<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">' +
  '<style>:root{color-scheme:light;padding:env(safe-area-inset-top,0px) 0 env(safe-area-inset-bottom,0px)}body{margin:0;font:14px system-ui}img{max-width:100%}[hidden]{display:none!important}</style></head><body>';
const server = http.createServer((req, res) => {
  if (req.url.startsWith('/app')) {
    res.writeHead(200, { 'content-type': 'text/html; charset=utf-8' });
    res.end(SKELETON + fs.readFileSync(path.join(__dirname, '..', 'app.html'), 'utf8') + '</body></html>');
  } else { res.writeHead(404); res.end(); }
});

// Which frame is on screen: the 12 squares at the top left of the picture.
const readCode = () => {
  const v = document.getElementById('video'), c = document.createElement('canvas');
  c.width = v.videoWidth; c.height = v.videoHeight;
  const g = c.getContext('2d');
  g.drawImage(v, 0, 0);
  let code = 0;
  for (let b = 0; b < 12; b++) { const p = g.getImageData(8 + 16 * b, 8, 1, 1).data; if (p[0] + p[1] + p[2] > 384) code |= 1 << b; }
  return code;
};

async function settled(page) {
  await page.evaluate(() => window.swimApp.player.shownFrame(window.swimApp.player.state().frame));
  await page.evaluate(() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r))));
}
async function shownCode(page) { await settled(page); return page.evaluate(readCode); }
async function go(page, k) { await page.evaluate(n => window.swimApp.player.go(n), k); await settled(page); }
const frame = page => page.evaluate(() => window.swimApp.player.state().shown);

async function openClip(page, file, athlete) {
  await page.setInputFiles('#pick-video', path.join(clips, file));
  await page.waitForFunction(() => { const s = window.swimApp.state; return s.view === 'setup' && (s.loadError || (s.loadedFor && !s.soundBusy)); }, null, { timeout: 60000 });
  if (athlete) await page.fill('#athlete', athlete);
}

(async () => {
  await new Promise(r => server.listen(0, r));
  const url = 'http://127.0.0.1:' + server.address().port + '/app';
  const browser = await chromium.launch();
  const context = await browser.newContext({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 1, isMobile: true, hasTouch: true, locale: 'el-GR' });
  // The save of files, as the platform gives it to the page: here it keeps what the page hands over.
  await context.addInitScript(() => {
    window.__saved = [];
    window.claude = { use: name => Promise.resolve(name === 'downloads' ? {
      save: async ({ filename, data }) => {
        const buf = typeof data === 'string' ? new TextEncoder().encode(data) : new Uint8Array(await new Response(data).arrayBuffer());
        let bin = ''; for (let i = 0; i < buf.length; i += 8192) bin += String.fromCharCode.apply(null, buf.subarray(i, i + 8192));
        window.__saved.push({ filename, base64: btoa(bin) });
        return { status: 'saved' };
      }
    } : null) };
  });
  const page = await context.newPage();
  const errors = [];
  page.on('console', m => { if (m.type() === 'error' && !/fonts\.g|ERR_|Failed to load resource/.test(m.text())) errors.push(m.text()); });
  page.on('pageerror', e => errors.push('pageerror: ' + e.message));
  await page.goto(url);
  await page.waitForFunction(() => window.swimApp && window.swimApp.state.view === 'home');
  check('home: opens empty', (await page.textContent('#home-list')).includes('Καμία ανάλυση ακόμα'));
  await page.screenshot({ path: path.join(shots, '01-home.png') });

  // ---------------- the whole way on the 60 fps clip
  await openClip(page, 'a_60_bf.mp4', 'Αθλητής Α');
  const status = await page.textContent('#setup-status');
  check('setup: frames read from the file', status.includes('60 fps') && status.includes('360 καρέ'), status.replace(/\s+/g, ' ').slice(0, 160));
  check('setup: signal heard', status.includes('Σήμα εκκίνησης από τον ήχο στα 2,01 s'), '');
  await page.click('#stroke-chips .chip >> text=Ελεύθερο');
  await page.screenshot({ path: path.join(shots, '02-setup.png') });
  await page.click('#setup-go');
  await page.waitForFunction(() => window.swimApp.state.view === 'mark');
  await settled(page);
  const sig = await page.evaluate(() => window.swimApp.state.A.signal);
  check('mark: signal from the sound within 4 ms', sig && sig.src === 'sound' && Math.abs(sig.t - 2.0137) < 0.004, sig ? `${sig.t.toFixed(4)} (${((sig.t - 2.0137) * 1000).toFixed(1)} ms) tones ${sig.low}/${sig.high} score ${sig.score}` : 'none');
  check('mark: opens on the frame of the signal', await frame(page) === 120 && await shownCode(page) === 120, `frame ${await frame(page)}, picture ${await shownCode(page)}`);
  check('mark: the next mark is selected', await page.evaluate(() => window.swimApp.state.sel) === 'react');
  await page.screenshot({ path: path.join(shots, '03-mark.png') });

  // Steps with the buttons: the picture is the frame asked, every time.
  let bad = [];
  const taps = [['#t-f1', 1], ['#t-f1', 1], ['#t-f1', 1], ['#t-b1', -1], ['#t-f10', 10], ['#t-b1', -1], ['#t-b10', -10], ['#t-b1', -1], ['#t-b1', -1], ['#t-f1', 1]];
  let want = 120;
  for (const [sel, d] of taps) {
    await page.click(sel); want += d;
    const code = await shownCode(page), k = await frame(page);
    if (code !== want || k !== want) bad.push(`${sel}: want ${want}, frame ${k}, picture ${code}`);
  }
  check('mark: ten steps, the picture follows', bad.length === 0, bad.join('; '));

  // The readout: time from the signal.
  await go(page, 162);
  const clock = await page.textContent('#clock');
  check('mark: clock from the signal', /^\+0,68[5-9]$/.test(clock), clock);

  // The marks of a start, each on its frame.
  const marks = { react: 132, takeoff: 162, entry: 182, m5: 212, m10: 270, m15: 330, breakout: 240 };
  for (const [id, k] of Object.entries(marks)) {
    await page.click(`#events .ev:nth-child(${['signal', 'react', 'takeoff', 'entry', 'm5', 'm10', 'm15', 'breakout'].indexOf(id) + 1})`);
    await go(page, k);
    await page.click('#mark-btn');
    await page.waitForFunction(i => { const a = window.swimApp.state.A; return a.marks[i] && typeof a.marks[i].t === 'number'; }, id);
  }
  const stored = await page.evaluate(() => window.swimApp.state.A.marks);
  check('mark: the times of the frames', Object.entries(marks).every(([id, k]) => Math.abs(stored[id].t - k / 60) < 1e-6), JSON.stringify(stored));
  await page.click('#events .ev:nth-child(3)');
  await settled(page);
  check('mark: a tap on a mark shows its frame', await shownCode(page) === 162 && (await page.textContent('#mark-btn')).includes('σε αυτό το καρέ'), await page.textContent('#mark-btn'));
  await page.screenshot({ path: path.join(shots, '04-marked.png') });

  // Undo of a mark moved by mistake.
  await go(page, 170);
  check('mark: on another frame the button moves the mark', (await page.textContent('#mark-btn')) === 'Μετακίνηση εδώ: Αποδέσμευση', await page.textContent('#mark-btn'));
  await page.click('#mark-btn');
  await page.waitForFunction(() => Math.abs(window.swimApp.state.A.marks.takeoff.t - 170 / 60) < 1e-6, null, { timeout: 5000 }).catch(() => {});
  check('mark: moved', Math.abs((await page.evaluate(() => window.swimApp.state.A.marks.takeoff.t)) - 170 / 60) < 1e-6);
  await page.click('#toast button');
  check('mark: undone', Math.abs((await page.evaluate(() => window.swimApp.state.A.marks.takeoff.t)) - 162 / 60) < 1e-6);

  // Playback and pause: stops on a frame, the picture is that frame.
  await go(page, 100);
  await page.click('#t-play');
  await page.waitForTimeout(700);
  await page.click('#t-play');
  const after = await frame(page), afterCode = await shownCode(page);
  check('mark: pause on a frame', after > 100 && after < 160 && afterCode === await frame(page), `frame ${after}, picture ${afterCode}`);

  // Zoom with two taps, back with the button.
  const box = await page.locator('#stage').boundingBox();
  await page.touchscreen.tap(box.x + 120, box.y + 80);
  await page.waitForTimeout(80);
  await page.touchscreen.tap(box.x + 120, box.y + 80);
  await page.waitForTimeout(100);
  check('mark: two taps zoom in', (await page.evaluate(() => document.getElementById('zoomer').style.transform)).includes('scale(2.5)'));
  await page.screenshot({ path: path.join(shots, '05-zoom.png') });
  await page.click('#zoom-out');
  check('mark: back to the whole picture', (await page.evaluate(() => document.getElementById('zoomer').style.transform)) === '');

  // A drag on the strip: 6 pixels a frame, to the left goes forward.
  await go(page, 200);
  const sb = await page.locator('#strip').boundingBox();
  await page.evaluate(([x, y]) => {
    const c = document.getElementById('strip'), ev = (type, dx) => c.dispatchEvent(new PointerEvent(type, { pointerId: 7, clientX: x + dx, clientY: y, bubbles: true, pointerType: 'touch', isPrimary: true }));
    ev('pointerdown', 0); ev('pointermove', -30); ev('pointermove', -60); ev('pointerup', -60);
  }, [sb.x + sb.width / 2, sb.y + sb.height / 2]);
  await settled(page);
  check('mark: drag of the strip', await frame(page) === 210 && await shownCode(page) === 210, `frame ${await frame(page)}`);

  // The measurements.
  await page.click('#mark-results');
  await page.waitForFunction(() => window.swimApp.state.view === 'result');
  const table = await page.$$eval('#result-body .trow', rows => rows.map(r => [r.querySelector('.k').textContent, r.querySelector('.v').textContent]));
  const expect = { 'Αντίδραση (πρώτη κίνηση)': '0,19s', 'Αποδέσμευση από τον βατήρα': '0,69s', 'Πτήση: αποδέσμευση → είσοδος': '0,33s', 'Είσοδος στο νερό': '1,02s',
    '5 μ.': '1,52s', '10 μ.': '2,49s', '15 μ.': '3,49s', 'Έξοδος από το νερό (κεφάλι)': '1,99s', 'Υποβρύχια φάση: είσοδος → έξοδος': '0,97s', 'Ταχύτητα 5 → 10 μ.': '5,17m/s', 'Ταχύτητα 10 → 15 μ.': '5,00m/s' };
  const got = Object.fromEntries(table);
  const wrong = Object.keys(expect).filter(k => got[k] !== expect[k]).map(k => `${k}: ${got[k]} (want ${expect[k]})`);
  check('result: every measurement', wrong.length === 0 && table.length === 11, wrong.join('; ') || table.length + ' rows');
  check('result: headline', (await page.textContent('#result-body .hero .big')).replace(/\s/g, '') === '3,49s');
  check('result: no warning', (await page.$$('#result-body .note')).length === 0, await page.$$eval('#result-body .note', n => n.map(e => e.textContent).join(' | ')));
  check('result: nothing wider than the phone', await page.evaluate(() => document.documentElement.scrollWidth) <= 390);
  await page.screenshot({ path: path.join(shots, '06a-result-top.png') });
  await page.fill('#notes', 'Δοκιμαστική σημείωση για την αναφορά.');
  await page.screenshot({ path: path.join(shots, '06-result.png'), fullPage: false });

  // Technique: two right, one to work on.
  await page.click('#result-tech');
  await page.locator('#tech-body .tp').nth(0).locator('.tp-btn.ok').click();
  await page.locator('#tech-body .tp').nth(1).locator('.tp-btn.ok').click();
  await page.locator('#tech-body .tp').nth(2).locator('.tp-btn.fix').click();
  const sum = await page.textContent('#tech-body .sum');
  check('tech: summary', sum.includes('2 από 3 σημεία σωστά') && sum.includes('1 σημείο θέλει δουλειά'), sum);
  check('tech: the correction shows', (await page.textContent('#tech-body')).includes('Διόρθωση: Σπρώξε πρώτα με το πίσω πόδι'));
  await page.screenshot({ path: path.join(shots, '07-tech.png') });

  // Report: three frames of the marks, saved as PDF and as picture.
  await page.click('#tech-report');
  await page.waitForFunction(() => window.swimApp.state.report && !window.swimApp.state.reportBusy, null, { timeout: 30000 });
  const rep = await page.evaluate(() => ({ thumbs: window.swimApp.state.report.thumbs, w: window.swimApp.state.report.canvas.width, h: window.swimApp.state.report.canvas.height }));
  check('report: drawn with three frames', rep.thumbs === 3 && rep.w === 1240 && rep.h === 1754, JSON.stringify(rep));
  await page.screenshot({ path: path.join(shots, '08-report.png') });
  fs.writeFileSync(path.join(shots, '09-report-page.png'), Buffer.from((await page.evaluate(() => window.swimApp.state.report.canvas.toDataURL('image/png'))).split(',')[1], 'base64'));
  await page.click('#report-pdf');
  await page.waitForFunction(() => window.__saved.length === 1);
  await page.click('#report-body .stack .btn:nth-child(1)');
  await page.waitForFunction(() => window.__saved.length === 2);
  const saved = await page.evaluate(() => window.__saved);
  const pdfPath = path.join(shots, 'report.pdf');
  fs.writeFileSync(pdfPath, Buffer.from(saved[0].base64, 'base64'));
  let info = '';
  try { info = execFileSync('pdfinfo', [pdfPath]).toString(); } catch (e) { info = String(e.message); }
  check('report: PDF of one A4 page', saved[0].filename === `Εκκίνηση Αθλητής Α ${new Date().toISOString().slice(0, 10)}.pdf` && /Pages:\s+1/.test(info) && /A4/.test(info), saved[0].filename + ' · ' + (info.match(/Page size:.*/) || [''])[0]);
  check('report: picture', saved[1].filename.endsWith('.png') && Buffer.from(saved[1].base64, 'base64').subarray(1, 4).toString() === 'PNG', saved[1].filename);
  check('report: the frame under the screens is still the one left', await page.evaluate(readCode) === 210);

  // Closed and opened again: the analysis is there, its marks too; the video is asked for again.
  await page.reload();
  await page.waitForFunction(() => window.swimApp && window.swimApp.state.view === 'home');
  const row = await page.textContent('#home-list .item');
  check('home: the analysis is kept', row.includes('Αθλητής Α') && row.includes('3,49 s') && row.includes('15 μ.'), row.replace(/\s+/g, ' '));
  await page.screenshot({ path: path.join(shots, '10-home-list.png') });
  await page.click('#home-list .item');
  await page.click('#result-marks');
  check('mark: asks for the video again', (await page.textContent('#stage-msg')).includes('διάλεξε ξανά το βίντεο'));
  await page.setInputFiles('#reopen-video', path.join(clips, 'a_60_bf.mp4'));
  await page.waitForFunction(() => { const s = window.swimApp.state; return s.loadedFor === s.A.id && !s.soundBusy; }, null, { timeout: 60000 });
  await settled(page);
  const again = await page.evaluate(() => window.swimApp.state.A);
  check('mark: the marks are untouched', Math.abs(again.marks.m15.t - 5.5) < 1e-6 && again.signal.src === 'sound' && Math.abs(again.signal.t - sig.t) < 1e-9);

  // Turned on its side.
  await page.setViewportSize({ width: 844, height: 390 });
  await page.waitForTimeout(200);
  const land = await page.evaluate(() => { const s = document.getElementById('stage').getBoundingClientRect(), p = document.querySelector('#v-mark .panel').getBoundingClientRect(), b = document.getElementById('mark-btn').getBoundingClientRect(); return { stage: [s.left, s.top, s.width, s.height], panel: [p.left, p.width], btnBottom: b.bottom, scrollW: document.documentElement.scrollWidth }; });
  check('landscape: picture left, controls right, all on screen', land.stage[2] > 500 && land.panel[0] >= land.stage[0] + land.stage[2] - 1 && land.btnBottom <= 390 && land.scrollW <= 844, JSON.stringify(land));
  await page.screenshot({ path: path.join(shots, '11-landscape.png') });
  await page.setViewportSize({ width: 390, height: 844 });

  // ---------------- the other clips: frames and signal
  const others = [
    ['b_5994_fast.mp4', 359, 2.0137, k => k, '59,94 fps'],
    ['c_30_nobf.mov', 180, 2.0137, k => k, '30 fps'],
    ['h_2997.mp4', 179, 2.0137, k => k, '29,97 fps'],
    ['e_240.mp4', 720, 1.0137, k => k, '240 fps'],
    ['f_offset.mp4', 360, 2.1137, k => k, '60 fps'],
    ['d_vfr.mp4', 309, 2.0137, k => { let n = -1, c = 0; for (;;) { n++; if (n % 7 === 3) continue; if (c === k) return n; c++; } }, '60 fps']
  ];
  for (const [file, frames, signalAt, codeOf, fps] of others) {
    await page.click('#mark-back');
    await openClip(page, file, 'Αθλήτρια Β');
    const st = await page.evaluate(() => ({ ...window.swimApp.player.state(), d: window.swimApp.player.diagnostics(), sound: { path: window.swimApp.state.sound.path, lead: window.swimApp.state.sound.lead, note: window.swimApp.state.sound.note, worker: window.swimApp.state.sound.worker }, signal: window.swimApp.state.A.signal }));
    const text = await page.textContent('#setup-status');
    check(`${file}: ${frames} frames from the file at ${fps}`, st.source === 'tables' && st.count === frames && text.includes(fps), `${st.source} ${st.count} · watch ${st.d.watch} · offset ${st.d.offset}`);
    check(`${file}: signal at ${signalAt}`, st.signal && Math.abs(st.signal.t - signalAt) < 0.004 && st.sound.path === 'stream' && st.sound.lead === 0,
      st.signal ? `${st.signal.t.toFixed(4)} (${((st.signal.t - signalAt) * 1000).toFixed(1)} ms) · ${st.sound.path} · lead ${st.sound.lead} · worker ${st.sound.worker}` : 'none ' + st.sound.note);
    await page.click('#setup-go');
    await page.waitForFunction(() => window.swimApp.state.view === 'mark');
    bad = [];
    const seq = [0, 1, 2, 3, 2, 1, 7, 8, 9, 10, 9, frames - 1, frames - 2, Math.floor(frames / 2), Math.floor(frames / 2) + 1, Math.floor(frames / 2) - 1, 20, 21, 22, 23, 24, 25, 24, 23];
    for (const k of seq) {
      await go(page, k);
      const code = await page.evaluate(readCode), shown = await frame(page);
      if (shown !== k || code !== codeOf(k)) bad.push(`asked ${k}: frame ${shown}, picture ${code} (want ${codeOf(k)})`);
    }
    const d = await page.evaluate(() => window.swimApp.player.diagnostics());
    check(`${file}: ${seq.length} jumps, the picture is the frame asked`, bad.length === 0 && d.corrected === 0, bad.slice(0, 4).join('; ') || `corrected ${d.corrected}`);
  }

  // ---------------- two signals as clear as each other: the app asks which one
  await page.click('#mark-back');
  await openClip(page, 'i_two.mp4', 'Αθλήτρια Β');
  check('two signals: none taken, both announced', await page.evaluate(() => window.swimApp.state.A.signal) === null && (await page.textContent('#setup-status')).includes('Ακούστηκαν 2 πιθανά σήματα'));
  await page.click('#setup-go');
  await page.waitForSelector('#backdrop:not([hidden])');
  const offered = await page.$$eval('#sheet .rowline b', n => n.map(e => e.textContent));
  check('two signals: the sheet lists them in order', offered.length === 2 && offered[0] === '1,50 s' && offered[1] === '4,01 s', offered.join(' | '));
  await page.screenshot({ path: path.join(shots, '13-two-signals.png') });
  await page.locator('#sheet .rowline').nth(1).locator('button', { hasText: 'Χρήση' }).click();
  await settled(page);
  const second = await page.evaluate(() => window.swimApp.state.A.signal);
  check('two signals: the one chosen is the start', second && second.src === 'sound' && Math.abs(second.t - 4.0137) < 0.004 && await frame(page) === 240 && await shownCode(page) === 240, second ? second.t.toFixed(4) : 'none');

  // ---------------- a clap and no signal: marked by hand where the sound rises
  await page.click('#mark-back');
  await openClip(page, 'k_clap.mp4', 'Αθλήτρια Β');
  check('no signal: says so', (await page.textContent('#setup-status')).includes('Δεν ακούστηκε κόρνα'));
  await page.click('#setup-go');
  await page.waitForFunction(() => window.swimApp.state.view === 'mark');
  check('no signal: the signal is the mark to make', (await page.textContent('#mark-btn')) === 'Σημάδεψε: Σήμα εκκίνησης' && await page.evaluate(() => document.getElementById('backdrop').hidden));
  // The loudest moment of the sound is the clap at 3.0 s: frame 180.
  const loudest = await page.evaluate(() => { const s = window.swimApp.state.sound, v = s.loud.values; let m = 0; for (let i = 1; i < v.length; i++) if (v[i] > v[m]) m = i; return s.loud.start + m * s.loud.step; });
  check('no signal: the clap is where it was made in the strip of the sound', Math.abs(loudest - 3.0) < 0.012, loudest.toFixed(4));
  await go(page, 180);
  await page.click('#mark-btn');
  await page.waitForFunction(() => window.swimApp.state.A.signal);
  const byHand = await page.evaluate(() => window.swimApp.state.A.signal);
  check('no signal: marked on the frame', byHand.src === 'frame' && Math.abs(byHand.t - 3.0) < 1e-6);
  await page.screenshot({ path: path.join(shots, '14-clap.png') });

  // ---------------- a video without sound, and a picture taller than wide
  await page.click('#mark-back');
  await openClip(page, 'j_silent.mp4', 'Αθλήτρια Β');
  const silent = await page.textContent('#setup-status');
  check('silent video: opens, the signal is left to the coach', silent.includes('240 καρέ') && silent.includes('Ο ήχος του βίντεο δεν διαβάστηκε') && !(await page.evaluate(() => document.getElementById('setup-go').disabled)), silent.replace(/\s+/g, ' ').slice(-90));
  await page.click('#setup-back');
  await openClip(page, 'l_tall.mp4', 'Αθλήτρια Β');
  await page.click('#setup-go');
  await page.waitForFunction(() => window.swimApp.state.view === 'mark');
  await settled(page);
  const tall = await page.evaluate(() => { const s = document.getElementById('stage').getBoundingClientRect(), b = document.getElementById('mark-btn').getBoundingClientRect(); return { w: s.width, h: s.height, btnBottom: b.bottom, events: document.getElementById('events').getBoundingClientRect().height }; });
  check('tall picture: the controls stay on screen', tall.h <= 0.47 * 844 && tall.btnBottom <= 844 && tall.events >= 44, JSON.stringify(tall));
  await page.screenshot({ path: path.join(shots, '15-tall.png') });

  // ---------------- the video of one analysis is never marked into another
  await page.click('#mark-back');
  await page.locator('#home-list .item', { hasText: 'Αθλητής Α' }).first().click();
  await page.click('#result-marks');
  const guard = await page.evaluate(() => ({ loaded: window.swimApp.player.state().loaded, loadedFor: window.swimApp.state.loadedFor, msg: document.getElementById('stage-msg').textContent, disabled: document.getElementById('mark-btn').disabled }));
  check('another analysis: its video is asked for, the one open is closed', !guard.loaded && guard.loadedFor === null && guard.msg.includes('διάλεξε ξανά το βίντεο') && guard.disabled, JSON.stringify(guard));

  // ---------------- a file without tables (WebM): frames measured from the playback, sound from the whole file
  await page.click('#mark-back');
  await openClip(page, 'g_vp9.webm', 'Αθλητής Γ');
  const web = await page.evaluate(() => ({ ...window.swimApp.player.state(), d: window.swimApp.player.diagnostics(), sound: window.swimApp.state.sound && { path: window.swimApp.state.sound.path, status: window.swimApp.state.sound.status, note: window.swimApp.state.sound.note }, signal: window.swimApp.state.A.signal }));
  check('webm: frames measured at 60 per second', web.source === 'measured' && Math.abs(web.fps - 60) < 0.5 && Math.abs(web.count - 360) <= 2, `${web.source} ${web.fps.toFixed(2)} fps ${web.count} frames · watch ${web.d.watch}`);
  check('webm: signal from the whole file', web.signal && Math.abs(web.signal.t - 2.0137) < 0.012 && web.sound.path === 'file', web.signal ? `${web.signal.t.toFixed(4)} · ${web.sound.path}` : JSON.stringify(web.sound));
  await page.click('#setup-go');
  await page.waitForFunction(() => window.swimApp.state.view === 'mark');
  bad = [];
  for (const k of [0, 1, 2, 30, 31, 32, 31, 200, 201, 199, 100]) {
    await go(page, k);
    const code = await page.evaluate(readCode), shown = await frame(page);
    if (shown !== k || code !== k) bad.push(`asked ${k}: frame ${shown}, picture ${code}`);
  }
  check('webm: the picture is the frame asked', bad.length === 0, bad.slice(0, 4).join('; '));

  // ---------------- a file that is not a video
  await page.click('#mark-back');
  fs.writeFileSync(path.join(shots, 'not-a-video.mp4'), 'this is not a video');
  await openClip(page, path.relative(clips, path.join(shots, 'not-a-video.mp4')), '');
  check('not a video: says so', (await page.textContent('#setup-status')).includes('δεν μπορεί να παίξει αυτό το βίντεο') && await page.evaluate(() => document.getElementById('setup-go').disabled));
  await page.click('#setup-back');

  // ---------------- settings: the list of technique, the copy for safekeeping
  await page.click('#home-settings');
  check('settings: example list', (await page.textContent('#settings-body')).includes('Λίστα-παράδειγμα'));
  const firstPoint = page.locator('#settings-body .edit-item input').first();
  await firstPoint.fill('Γοφοί ψηλά στη θέση ετοιμότητας');
  await firstPoint.dispatchEvent('change');
  check('settings: a point renamed is kept', await page.evaluate(() => window.swimApp.db().tech.phases[0].items[0].name) === 'Γοφοί ψηλά στη θέση ετοιμότητας');
  await page.screenshot({ path: path.join(shots, '12-settings.png') });
  const before = await page.evaluate(() => window.__saved.length);
  await page.click('#settings-body button:has-text("Αποθήκευση αντιγράφου")');
  await page.waitForFunction(n => window.__saved.length === n + 1, before);
  const copy = JSON.parse(Buffer.from((await page.evaluate(() => window.__saved[window.__saved.length - 1])).base64, 'base64').toString('utf8'));
  check('settings: copy for safekeeping', copy.format === 'kinovea-swim-mobile' && copy.analyses.length >= 2 && copy.analyses.some(a => a.athlete === 'Αθλητής Α'), `${copy.analyses.length} analyses`);
  const diag = await page.textContent('#settings-body .diag');
  console.log('     ' + diag.replace(/\n/g, '\n     '));

  check('no error in the console', errors.length === 0, errors.slice(0, 5).join(' | '));
  await browser.close();
  server.close();
  console.log(failed ? `\n${failed} FAILED` : '\nall ok');
  process.exit(failed ? 1 : 0);
})().catch(e => { console.error('TEST CRASHED', e); process.exit(1); });
