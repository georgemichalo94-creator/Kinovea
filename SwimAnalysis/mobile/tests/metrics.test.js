// Checks of the measurements and of the PDF writer. Fictional athletes only.
'use strict';
const fs = require('fs'), os = require('os'), path = require('path'), { execFileSync } = require('child_process');
const M = require('../src/metrics.js')(), pdf = require('../src/pdf.js')();
let failed = 0;
function check(name, ok, detail) { console.log((ok ? 'ok   ' : 'FAIL ') + name + (detail ? '  ' + detail : '')); if (!ok) failed++; }
function eq(a, b) { return a === b || (a !== null && b !== null && Math.abs(a - b) < 1e-9); }

const a = {
  id: 'a2', created: '2026-11-20T09:00:00.000Z', athlete: 'Αθλητής Α', stroke: 'fs',
  signal: { t: 2.0137, src: 'sound', dist: 0 },
  marks: { react: { t: 2.2 }, takeoff: { t: 2.7 }, entry: { t: 3.0333 }, m5: { t: 3.5333 }, m10: { t: 5.8667 }, m15: { t: 8.4333 }, breakout: { t: 7.1 } }
};
const m = M.measure(a);
check('from the signal', eq(m.react, 0.19) && eq(m.block, 0.69) && eq(m.entry, 1.02) && eq(m.m5, 1.52) && eq(m.m10, 3.85) && eq(m.m15, 6.42) && eq(m.breakout, 5.09), JSON.stringify(m));
check('between marks', eq(m.blockPhase, 0.5) && eq(m.flight, 0.33) && eq(m.underwater, 4.07));
check('speeds', eq(m.v510, 2.14) && eq(m.v1015, 1.95), `${m.v510} ${m.v1015}`);
check('no warning on a sound start', M.warnings(a).length === 0, M.warnings(a).join(' | '));

// The sound travels: 10 m from the loudspeaker, the signal was given 29 ms before it was heard.
const far = JSON.parse(JSON.stringify(a)); far.signal.dist = 10;
check('distance from the loudspeaker', eq(M.measure(far).m15, 6.45) && eq(M.measure(far).flight, 0.33), String(M.measure(far).m15));
const byEye = JSON.parse(JSON.stringify(far)); byEye.signal.src = 'frame';
check('a signal marked on a frame is not moved', eq(M.measure(byEye).m15, 6.42));

// Marks missing: only what can be measured.
const part = { id: 'p', created: '2026-11-20T10:00:00.000Z', athlete: 'Αθλήτρια Β', signal: null, marks: { takeoff: { t: 1.0 }, entry: { t: 1.35 }, m5: { t: 2 }, m10: { t: 4.5 } } };
const mp = M.measure(part);
check('without the signal', mp.block === null && mp.m5 === null && eq(mp.flight, 0.35) && eq(mp.v510, 2) && mp.v1015 === null);
check('headline without the signal', M.headline(part).id === 'flight' && M.headline(a).id === 'm15' && M.headline({ marks: {} }) === null);

// Marks out of order, and a signal that cannot be right.
const wrong = JSON.parse(JSON.stringify(a)); wrong.marks.entry.t = 2.5; wrong.marks.breakout.t = 2.4;
const w = M.warnings(wrong);
check('entry before the take-off', w.some(s => s.includes('«Είσοδος» πριν από «Αποδέσμευση»')) && w.some(s => s.includes('«Έξοδος» πριν από «Είσοδος»')), w.join(' | '));
const late = JSON.parse(JSON.stringify(a)); late.signal.t = 1.2;
check('take-off 1.5 s after the signal', M.warnings(late).some(s => s.includes('συνήθως 0,55–0,95 s')), M.warnings(late).join(' | '));

// Writing of numbers.
check('numbers', M.fmt(6.4) === '6,40' && M.fmt(null) === '—' && M.fmt(-0.5) === '−0,50' && M.fmt(2.14, 1) === '2,1');
check('differences', M.fmtDelta(6.42, 6.53) === '−0,11' && M.fmtDelta(3.86, 3.83) === '+0,03' && M.fmtDelta(1, 1) === '0,00' && M.fmtDelta(1, null) === '');
check('hundredths', M.round2(0.685) === 0.69 && M.round2(-0.685) === -0.69 && M.round2(1.005) === 1.01 && M.round2(2.675) === 2.68);

// The analysis before, and the last five.
const older = n => ({ id: 'o' + n, created: `2026-11-1${n}T09:00:00.000Z`, athlete: ' αθλητής α ', signal: { t: 1, src: 'frame' }, marks: { m15: { t: 7.5 + n / 100 } } });
const list = [older(1), older(2), older(3), older(4), older(5), a, part, { id: 'x', created: '2026-11-19T09:00:00.000Z', athlete: 'Αθλητής Α', signal: null, marks: {} }];
check('the one before', M.previous(list, a, 'm15').id === 'o5' && M.previous(list, a, 'flight') === null && M.previous(list, part, 'flight') === null);
const h = M.history(list, a, 'm15', 5);
check('the last five', h.length === 5 && h[0].id === 'o2' && h[4].id === 'a2' && eq(h[4].value, 6.42), h.map(r => r.id + ':' + r.value).join(' '));

const text = M.summary(a, { ok: 7, checked: 9, fix: ['Γωνία εισόδου', 'Πόδια μαζί'] });
check('summary', text.includes('Εκκίνηση — Αθλητής Α · ελεύθερο · 20/11/2026') && text.includes('15 μ. 6,42 s') && text.includes('7 από 9 σημεία σωστά') && text.includes('5→10 μ. 2,14 m/s'), '\n' + text);

// The PDF: a JPEG of the proportions of A4 inside one page.
const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'swimpdf-')), jpg = path.join(tmp, 'p.jpg'), out = path.join(tmp, 'p.pdf');
execFileSync('ffmpeg', ['-hide_banner', '-loglevel', 'error', '-y', '-f', 'lavfi', '-i', 'testsrc2=size=1240x1754:rate=1', '-frames:v', '1', jpg]);
fs.writeFileSync(out, pdf.fromJpeg(new Uint8Array(fs.readFileSync(jpg)), 1240, 1754, 'Εκκίνηση Αθλητής Α'));
let info = '';
try { info = execFileSync('pdfinfo', [out]).toString(); } catch (e) { info = 'pdfinfo: ' + e.message; }
check('pdf: one A4 page with its title', /Pages:\s+1/.test(info) && /Page size:\s+595\.28 x 841\.89/.test(info) && info.includes('Εκκίνηση Αθλητής Α'), info.split('\n').filter(l => /Title|Pages|Page size/.test(l)).join(' | '));
try {
  execFileSync('pdftoppm', ['-r', '20', '-png', out, path.join(tmp, 'page')]);
  const png = fs.readdirSync(tmp).find(f => f.endsWith('.png'));
  check('pdf: the page draws', !!png && fs.statSync(path.join(tmp, png)).size > 2000, png ? fs.statSync(path.join(tmp, png)).size + ' bytes' : '');
} catch (e) { check('pdf: the page draws', false, e.message); }
fs.rmSync(tmp, { recursive: true, force: true });

console.log(failed ? `\n${failed} FAILED` : '\nall ok');
process.exit(failed ? 1 : 0);
