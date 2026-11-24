// The report templates: one HTML document per race, A4 portrait pages, Greek. Fictional data only (data.js).
'use strict';
const { R, STYLE_NAME, IM_ORDER } = require('./data');
const ch = require('./charts');
const { C, esc } = ch;

const W = 722; // content width (px)

// ---- formats
const T = x => {
  if (x === null || x === undefined || !isFinite(x)) return '–';
  if (x < 60) return (Math.round(x * 100) / 100).toFixed(2);
  let m = Math.floor(x / 60), s = Math.round((x - m * 60) * 100) / 100;
  if (s >= 60) { m++; s = 0; }
  return m + ':' + (s < 10 ? '0' : '') + s.toFixed(2);
};
const F = (x, n) => x === null || x === undefined || !isFinite(x) ? '–' : x.toFixed(n === undefined ? 2 : n);
const S = (x, n) => {
  if (x === null || x === undefined || !isFinite(x)) return '–';
  const k = Math.pow(10, n === undefined ? 2 : n), a = Math.round(Math.abs(x) * k) / k;
  return (a === 0 ? '±' : x < 0 ? '−' : '+') + a.toFixed(n === undefined ? 2 : n);
};
const pct = x => (x * 100).toFixed(1).replace('-', '−') + '%';
// Delta chip: better 'lower' (times) or 'higher' (speeds, lengths); neutral: grey.
function chip(x, better, n, suffix) {
  if (x === null || x === undefined || !isFinite(x)) return '';
  const k = Math.pow(10, n === undefined ? 2 : n);
  const zero = Math.round(Math.abs(x) * k) === 0;
  const good = better === 'lower' ? x < 0 : better === 'higher' ? x > 0 : null;
  const cls = zero || good === null ? 'chip n' : good ? 'chip g' : 'chip l';
  return `<span class="${cls}">${S(x, n)}${suffix || ''}</span>`;
}

const META = {
  '50': { title: '50μ. Ελεύθερο', short: '50 ελ.' },
  '100': { title: '100μ. Ελεύθερο', short: '100 ελ.' },
  '200': { title: '200μ. Ελεύθερο', short: '200 ελ.' },
  '400': { title: '400μ. Ελεύθερο', short: '400 ελ.' },
  '400im': { title: '400μ. Μικτή Ατομική', short: '400 μικτή' },
  '800': { title: '800μ. Ελεύθερο', short: '800 ελ.' },
  '1500': { title: '1500μ. Ελεύθερο', short: '1500 ελ.' }
};
const ATHLETE = { name: 'Αθλητής Δείγμα', club: 'Κολυμβητικός Όμιλος Δείγμα', cat: 'Ανδρών', comp: 'Αγώνας δείγμα', date: 'Δεκέμβριος 2026',
  round: 'Τελικός Α', lane: '4', place: '2ος' };
const PB_NAME = 'Ατομικό ρεκόρ', REF_NAME = 'Αναφορά';

// ---- page frame
function band(x, key) {
  const r = x.race;
  const pb = x.pb.final, ref = x.ref.final;
  return `<div class="band">
    <div class="band-top"><span class="kicker">ΑΝΑΛΥΣΗ ΑΓΩΝΑ · RACE ANALYSIS</span><span class="proto">ΠΡΟΤΥΠΟ · ΦΑΝΤΑΣΤΙΚΑ ΔΕΔΟΜΕΝΑ</span></div>
    <div class="band-main">
      <div class="who">
        <div class="event">${esc(META[key].title)} <span class="cat">${esc(ATHLETE.cat)}</span></div>
        <div class="name">${esc(ATHLETE.name)}</div>
        <div class="meta">${esc(ATHLETE.club)} · ${esc(ATHLETE.comp)} · ${esc(ATHLETE.date)} · Πισίνα 50μ. · ${esc(ATHLETE.round)} · Διαδρομή ${esc(ATHLETE.lane)}</div>
      </div>
      <div class="hero">
        <div class="hero-lab">Τελικός χρόνος · ${esc(ATHLETE.place)}</div>
        <div class="hero-time">${T(r.final)}</div>
        <div class="hero-sub"><span>ΑΡ ${T(pb)} ${chip(r.final - pb, 'lower')}</span><span>Αναφ. ${T(ref)} ${chip(r.final - ref, 'lower')}</span></div>
      </div>
    </div>
  </div>`;
}

function head(key, title, n, N) {
  return `<div class="ph"><div class="ph-l"><span class="ph-ev">${esc(META[key].title)}</span><span class="ph-sep">·</span>${esc(ATHLETE.name)}</div>
    <div class="ph-r">${esc(title)}</div></div>`;
}

function foot(n, N) {
  return `<div class="pf"><span>Kinovea Swim · Ανάλυση αγώνα · Πρότυπο αναφοράς με φανταστικά δεδομένα</span><span>Σελίδα ${n} από ${N}</span></div>`;
}

function section(title, sub) {
  return `<div class="sec"><div class="sec-t">${esc(title)}</div>${sub ? `<div class="sec-s">${sub}</div>` : ''}</div>`;
}

// ---- comparisons
function groups(x) {
  // Phases of the race grouped for the comparison: short races phase by phase, long races by part.
  const D = x.race.p.distance;
  const step = D <= 200 ? 50 : D <= 400 ? 100 : D <= 800 ? 200 : 500;
  const out = [];
  const add = (label, pick, note) => {
    const t = list => list.filter(pick).reduce((a, p) => a + p.t, 0);
    out.push({ label, a: t(x.race.phases), pb: t(x.pb.phases), ref: t(x.ref.phases), note });
  };
  add('Εκκίνηση 0–15 μ.', p => p.kind === 'start');
  if (D === 50) add('Κολύμβηση 15–45 μ.', p => p.kind === 'swim');
  else {
    for (let a = 0; a < D; a += step) {
      const b = a + step;
      add(`Κολύμβηση ${a}–${b} μ.`, p => p.kind === 'swim' && p.from >= a && p.to <= b + 1e-9);
      if (step === 50) { if (b < D) add(`Στροφή ${b} μ.`, p => p.kind === 'turn' && p.wall === b); }
      else {
        const last = Math.min(b, D - 50);
        if (last > a) add(last > a + 50 ? `Στροφές ${a + 50}–${last} μ.` : `Στροφή ${last} μ.`, p => p.kind === 'turn' && p.wall > a && p.wall <= last);
      }
    }
  }
  add('Τερματισμός 5 μ.', p => p.kind === 'finish');
  return out.filter(g => g.a > 0);
}

function composition(x) {
  const parts = c => [
    { key: 'start', name: 'Εκκίνηση', value: c.start, color: C.p1 },
    { key: 'swim', name: 'Κολύμβηση', value: c.swim, color: C.p2 },
    { key: 'turn', name: 'Στροφές', value: c.turn, color: C.p3 },
    { key: 'finish', name: 'Τερματισμός', value: c.finish, color: C.p4 }
  ].filter(p => p.value > 0);
  const lg = parts(x.race.composition).map(p => `<span class="lgi"><i style="background:${p.color}"></i>${esc(p.name)}</span>`).join('');
  return ch.stacked({ w: W - 24, fmtT: v => T(v) + ' s', rows: [
    { label: 'Αυτός ο αγώνας', parts: parts(x.race.composition), total: x.race.final },
    { label: PB_NAME, parts: parts(x.pb.composition), total: x.pb.final },
    { label: REF_NAME, parts: parts(x.ref.composition), total: x.ref.final }
  ] }) + `<div class="lgrow">${lg}</div>`;
}

// ---- the KPI tiles
function tile(label, value, unit, dPb, dRef, better, n, extra) {
  const deltas = dPb === null && dRef === null ? '' : `<div class="t-d"><span>ΑΡ ${chip(dPb, better, n) || '–'}</span><span>Αναφ. ${chip(dRef, better, n) || '–'}</span></div>`;
  return `<div class="tile"><div class="t-lab">${esc(label)}</div><div class="t-val">${value}<span class="t-u">${esc(unit || '')}</span></div>
    ${deltas}${extra ? `<div class="t-x">${extra}</div>` : ''}</div>`;
}

function kpis(x) {
  const a = x.race, b = x.pb, c = x.ref, D = a.p.distance;
  const tiles = [];
  tiles.push(tile('Εκκίνηση 15 μ.', T(a.start.t15), ' s', a.start.t15 - b.start.t15, a.start.t15 - c.start.t15, 'lower'));
  if (a.turns.length) tiles.push(tile('Στροφή −5/+15 (μ.ο.)', T(a.avg.turnInOut), ' s', a.avg.turnInOut - b.avg.turnInOut, a.avg.turnInOut - c.avg.turnInOut, 'lower'));
  else tiles.push(tile('Απόσταση εξόδου', F(a.start.bd, 1), ' μ.', a.start.bd - b.start.bd, a.start.bd - c.start.bd, 'higher', 1));
  tiles.push(tile('Ταχύτητα κολύμβησης', F(a.avg.vClean), ' m/s', a.avg.vClean - b.avg.vClean, a.avg.vClean - c.avg.vClean, 'higher'));
  tiles.push(tile('Τερματισμός 5 μ.', T(a.finish.last5), ' s', a.finish.last5 - b.finish.last5, a.finish.last5 - c.finish.last5, 'lower'));
  tiles.push(tile('Συχνότητα', F(a.avg.sr, 1), ' κύκλ./λ.', a.avg.sr - b.avg.sr, a.avg.sr - c.avg.sr, 'none', 1));
  tiles.push(tile('Tempo', F(a.avg.tempo), ' s/κύκλο', a.avg.tempo - b.avg.tempo, a.avg.tempo - c.avg.tempo, 'none'));
  tiles.push(tile('Μήκος κύκλου', F(a.avg.sl), ' μ.', a.avg.sl - b.avg.sl, a.avg.sl - c.avg.sl, 'higher'));
  tiles.push(tile('ΔΑΚ (μ.ο. 50άρι)', F(a.avg.dak), '', a.avg.dak - b.avg.dak, a.avg.dak - c.avg.dak, 'lower'));
  return `<div class="tiles">${tiles.join('')}</div>`;
}

// ---- findings
function findings(x) {
  const a = x.race, b = x.pb, c = x.ref, D = a.p.distance;
  const good = [], work = [], aim = [];
  const dPb = a.final - b.final;
  if (dPb < -0.005) good.push(`<b>Νέο ατομικό ρεκόρ</b>: ${S(dPb)} s (${T(b.final)} → ${T(a.final)}).`);
  const NAME = { start: 'Εκκίνηση', swim: 'Κολύμβηση', turn: 'Στροφές', finish: 'Τερματισμός' };
  const tAvg = (r, f) => r.turns.reduce((p, q) => p + q[f], 0) / Math.max(1, r.turns.length);
  const why = (k, o, who) => {
    if (k === 'start') return `15 μ. σε ${F(a.start.t15)} s (${who} ${F(o.start.t15)}) · έξοδος στα ${F(a.start.bd, 1)} μ. (${who} ${F(o.start.bd, 1)}), ${a.start.kicks} δελφινισμοί (${who} ${o.start.kicks})`;
    if (k === 'turn') return `−5/+15 κατά μ.ο. ${F(tAvg(a, 'inOut'))} s (${who} ${F(tAvg(o, 'inOut'))}) · έξοδος στα ${F(tAvg(a, 'bd'), 1)} μ. (${who} ${F(tAvg(o, 'bd'), 1)})`;
    if (k === 'swim') return `ταχύτητα ${F(a.avg.vClean)} m/s (${who} ${F(o.avg.vClean)}) · μήκος κύκλου ${F(a.avg.sl)} μ. (${who} ${F(o.avg.sl)}) · συχνότητα ${F(a.avg.sr, 1)} (${who} ${F(o.avg.sr, 1)})`;
    return `τελευταία 5 μ. σε ${F(a.finish.last5)} s (${who} ${F(o.finish.last5)}) · τελευταία χεριά → επαφή ${F(a.finish.lastGap)} s (${who} ${F(o.finish.lastGap)})`;
  };
  const kinds = ['start', 'swim', 'turn', 'finish'].filter(k => a.composition[k] > 0);
  kinds.map(k => ({ k, d: a.composition[k] - b.composition[k] })).filter(o => o.d < -0.05).sort((p, q) => p.d - q.d)
    .forEach(o => good.push(`<b>${NAME[o.k]}</b>: ${S(o.d)} s από το ΑΡ · ${why(o.k, b, 'ΑΡ')}.`));
  if (D >= 200 && a.p.style !== 'im') {
    const mid = a.blocks.slice(1, -1).map(bk => bk.time);
    const mean = mid.reduce((p, q) => p + q, 0) / mid.length;
    const sd = Math.sqrt(mid.reduce((p, q) => p + (q - mean) * (q - mean), 0) / mid.length);
    if (sd / mean < 0.012) good.push(`<b>Σταθερός ρυθμός</b>: τα 50άρια από το 2ο ως το προτελευταίο μέσα σε ${F(Math.max(...mid) - Math.min(...mid))} s (διακύμανση ${(100 * sd / mean).toFixed(1)}%).`);
  }
  const legs = a.p.style === 'im' ? IM_ORDER.map((st, i) => {
    const s0 = i * 100, e = s0 + 100, t = r => r.byD[e].t - (s0 === 0 ? 0 : r.byD[s0].t);
    return { st, dPb: t(a) - t(b), dRef: t(a) - t(c) };
  }) : [];
  if (legs.length) {
    const best = legs.slice().sort((p, q) => p.dPb - q.dPb)[0];
    if (best.dPb < -0.05) good.push(`<b>${STYLE_NAME[best.st]}</b>: ${S(best.dPb)} s από το ΑΡ στο 100άρι του.`);
    const worst = legs.slice().sort((p, q) => q.dRef - p.dRef)[0];
    if (worst.dRef > 0.05) work.push(`<b>${STYLE_NAME[worst.st]}</b>: ${S(worst.dRef)} s από την αναφορά στο 100άρι του, η μεγαλύτερη διαφορά της μικτής.`);
  }
  kinds.map(k => ({ k, d: a.composition[k] - c.composition[k] })).filter(o => o.d > 0.05).sort((p, q) => q.d - p.d)
    .forEach(o => work.push(`<b>${NAME[o.k]}</b>: ${why(o.k, c, 'αναφ.')}.`));
  if (a.p.style !== 'im') {
    const free = a.rows.filter(r => r.isFree);
    const from = D <= 200 ? free.reduce((p, q) => q.vel > p.vel ? q : p) : free.find(r => r.prevD >= 50) || free[0];
    const to = D <= 200 ? free[free.length - 1] : free.filter(r => r.d > from.d).reduce((p, q) => q.vel < p.vel ? q : p, free[free.length - 1]);
    const drop = (from.vel - to.vel) / from.vel;
    if (drop >= 0.03) work.push(`<b>Ταχύτητα κολύμβησης</b>: έπεσε ${(drop * 100).toFixed(0)}% από τα ${from.prevD}–${from.d} ως τα ${to.prevD}–${to.d} μ. (${F(from.vel)} → ${F(to.vel)} m/s)· συχνότητα ${F(from.sr, 1)} → ${F(to.sr, 1)}, μήκος κύκλου ${F(from.sl)} → ${F(to.sl)} μ.`);
  }
  kinds.map(k => ({ k, d: a.composition[k] - c.composition[k] })).filter(o => o.d > 0.02).sort((p, q) => q.d - p.d).slice(0, 4)
    .forEach(o => aim.push(`${NAME[o.k]} όπως στην αναφορά: <b>${S(-o.d)} s</b> → ${T(a.final - o.d)}`));
  kinds.map(k => ({ k, d: a.composition[k] - b.composition[k] })).filter(o => o.d > 0.05).sort((p, q) => q.d - p.d).slice(0, 1)
    .forEach(o => aim.push(`${NAME[o.k]} όπως στο ΑΡ: <b>${S(-o.d)} s</b> → ${T(a.final - o.d)}`));
  const gsAll = groups(x), best = gsAll.reduce((p, g) => p + Math.min(g.a, g.pb), 0);
  if (best < a.final - 0.02) aim.splice(Math.min(aim.length, 3), 0, `Με τις καλύτερες φάσεις του (αγώνας + ΑΡ): <b>${T(best)}</b>`);
  const card = (cls, title, list) => `<div class="fc ${cls}"><div class="fc-t">${esc(title)}</div><ul>${list.slice(0, 4).map(t => `<li>${t}</li>`).join('')}</ul></div>`;
  return `<div class="finds">${card('good', 'Τι πήγε καλά', good.length ? good : ['—'])}${card('work', 'Τι να δουλέψουμε', work.length ? work : ['—'])}${card('aim', 'Περιθώριο βελτίωσης', aim.length ? aim : ['—'])}</div>`;
}

// ---- page 1: overview
function pageOverview(x, key, n, N) {
  const gs = groups(x);
  const items = gs.map(g => ({ label: g.label, value: g.a - g.pb, note: `Αναφ. ${S(g.a - g.ref)}` }));
  return `<div class="page">${band(x, key)}
    ${section('Με μια ματιά', 'Οι βασικοί δείκτες του αγώνα και η διαφορά τους από το ατομικό ρεκόρ (ΑΡ) και την αναφορά. Μπλε: καλύτερα · πορτοκαλί: χειρότερα.')}
    ${kpis(x)}
    ${section('Σύνθεση του χρόνου', 'Πού πήγε ο χρόνος του αγώνα: εκκίνηση (0–15 μ.), καθαρή κολύμβηση, στροφές (−5/+15) και τερματισμός (τελευταία 5 μ.).')}
    <div class="box">${composition(x)}</div>
    ${section('Πού κερδήθηκε και πού χάθηκε χρόνος', 'Διαφορά κάθε φάσης από το ΑΡ (μπάρες) και από την αναφορά (δεξιά).')}
    <div class="box tight">${ch.hDiverging({ w: W - 24, items, labelW: 150, noteW: 92, rowH: gs.length > 9 ? 19 : 21 })}</div>
    ${findings(x)}
    ${foot(n, N)}</div>`;
}

// ---- velocities by phase (short races)
function phaseSpeeds(a) {
  const out = [];
  a.rows.forEach(r => out.push({ cat: r.prevD + '–' + r.d, v: r.vel, free: r.isFree }));
  return out;
}

// ---- the 50: speed and strokes on one page
function pageSpeed50(x, key, n, N) {
  const a = x.race, b = x.pb, c = x.ref;
  const sp = phaseSpeeds(a), spb = phaseSpeeds(b), spr = phaseSpeeds(c);
  const series = (f, from) => [{ name: 'Αυτός ο αγώνας', color: C.race, values: from(a).map(f) }, { name: PB_NAME, color: C.pb, dash: 'dash', marker: 'square', values: from(b).map(f) },
    { name: REF_NAME, color: C.ref, dash: 'dot', marker: 'diamond', values: from(c).map(f) }];
  let body = section('Καμπύλη ταχύτητας', 'Μέση ταχύτητα κάθε τμήματος. Η εκκίνηση (0–15 μ.) και ο τερματισμός (45–50 μ.) περιέχουν πτήση, υποβρύχια φάση ή γλίστρημα.');
  body += `<div class="box">${ch.lineChart({ w: W - 24, h: 215, cats: sp.map(s => s.cat + ' μ.'), fmt: v => v.toFixed(2), yTitle: 'm/s',
    series: [{ name: 'Αυτός ο αγώνας', color: C.race, values: sp.map(s => s.v) }, { name: PB_NAME, color: C.pb, dash: 'dash', marker: 'square', values: spb.map(s => s.v) },
      { name: REF_NAME, color: C.ref, dash: 'dot', marker: 'diamond', values: spr.map(s => s.v) }] })}</div>`;
  const free = a.rows.filter(r => r.isFree);
  const vmax = free.reduce((p, q) => q.vel > p.vel ? q : p), vlast = free[free.length - 1];
  body += `<div class="tiles t4">
    ${tile('Μέγιστη ταχύτητα', F(vmax.vel), ' m/s', vmax.vel - b.byD[vmax.d].vel, vmax.vel - c.byD[vmax.d].vel, 'higher', 2, `στα ${vmax.prevD}–${vmax.d} μ.`)}
    ${tile('Ταχύτητα 35–45 μ.', F(vlast.vel), ' m/s', vlast.vel - b.byD[vlast.d].vel, vlast.vel - c.byD[vlast.d].vel, 'higher')}
    ${tile('Πτώση ταχύτητας', ((vmax.vel - vlast.vel) / vmax.vel * 100).toFixed(1), '%', null, null, 'none', 1, `από τα ${vmax.prevD}–${vmax.d} στα 35–45 μ. (ΑΡ ${(100 * (1 - b.byD[45].vel / Math.max(...b.rows.filter(r => r.isFree).map(r => r.vel)))).toFixed(1)}%, αναφ. ${(100 * (1 - c.byD[45].vel / Math.max(...c.rows.filter(r => r.isFree).map(r => r.vel)))).toFixed(1)}%)`)}
    ${tile('Ταχύτητα τελευταίων 5 μ.', F(a.finish.vLast5), ' m/s', a.finish.vLast5 - b.finish.vLast5, a.finish.vLast5 - c.finish.vLast5, 'higher')}
  </div>`;
  const fr = r => r.rows.filter(q => q.isFree);
  const cats = fr(a).map(r => r.prevD + '–' + r.d + ' μ.');
  body += section('Συχνότητα και μήκος κύκλου', 'Στην καθαρή κολύμβηση (15–45 μ.): ταχύτητα = συχνότητα × μήκος κύκλου.');
  body += `<div class="two"><div class="box">${ch.lineChart({ w: (W - 8) / 2 - 24, h: 175, cats, fmt: v => v.toFixed(1), yTitle: 'Συχνότητα (κύκλοι/λεπτό)', series: series(r => r.sr, fr), legendOff: true, mr: 40 })}</div>
    <div class="box">${ch.lineChart({ w: (W - 8) / 2 - 24, h: 175, cats, fmt: v => v.toFixed(2), yTitle: 'Μήκος κύκλου (μ.)', series: series(r => r.sl, fr), legendOff: true, mr: 40 })}</div></div>`;
  body += `<div class="lgrow c">${[['Αυτός ο αγώνας', C.race, ''], [PB_NAME, C.pb, 'dash'], [REF_NAME, C.ref, 'dot']].map(([t, col, d]) => `<span class="lgi"><b class="ln ${d}" style="border-color:${col}"></b>${esc(t)}</span>`).join('')}</div>`;
  body += section('Οι χεριές ανά τμήμα', '');
  body += strokeTable(x);
  return `<div class="page">${head(key, 'Ταχύτητα και χεριές')}${body}${foot(n, N)}</div>`;
}

function strokeTable(x) {
  const a = x.race, b = x.pb;
  const free = a.rows.filter(q => q.isFree);
  return `<div class="box"><table class="tbl"><thead><tr><th>Τμήμα</th><th>Χρόνος</th><th>Ταχύτητα<br>m/s</th><th>Χεριές</th><th>Συχνότητα<br>κύκλ./λ.</th><th>Tempo<br>s/κύκλο</th><th>Μήκος κύκλου<br>μ.</th><th>Απόσταση<br>ανά χεριά</th><th>SI<br>m²/s</th><th>Δ ταχύτητας<br>από ΑΡ</th></tr></thead><tbody>
      ${free.map(r => `<tr><td class="l">${r.prevD}–${r.d} μ.</td><td>${F(r.seg)}</td><td><b>${F(r.vel)}</b></td><td>${F(r.strokes, 1)}</td><td>${F(r.sr, 1)}</td><td>${F(r.tempo)}</td><td>${F(r.sl)}</td><td>${F(r.dps)}</td><td>${F(r.si)}</td><td>${chip(r.vel - b.byD[r.d].vel, 'higher')}</td></tr>`).join('')}
      <tr class="tot"><td class="l">Μέσος όρος</td><td></td><td><b>${F(a.avg.vClean)}</b></td><td></td><td>${F(a.avg.sr, 1)}</td><td>${F(a.avg.tempo)}</td><td>${F(a.avg.sl)}</td><td>${F(a.avg.sl / 2)}</td><td>${F(a.avg.si)}</td><td>${chip(a.avg.vClean - b.avg.vClean, 'higher')}</td></tr>
      </tbody></table></div>`;
}

// ---- page: pace
function pagePace(x, key, n, N) {
  const a = x.race, b = x.pb, c = x.ref, D = a.p.distance;
  let body = '';
  if (D === 100) {
    const sp = phaseSpeeds(a), spb = phaseSpeeds(b), spr = phaseSpeeds(c);
    body += section('Καμπύλη ταχύτητας', 'Μέση ταχύτητα κάθε τμήματος. Τα τμήματα εκκίνησης, στροφής και τερματισμού περιέχουν πτήση, υποβρύχια φάση ή γλίστρημα.');
    body += `<div class="box">${ch.lineChart({ w: W - 24, h: 235, cats: sp.map(s => s.cat), fmt: v => v.toFixed(2), yTitle: 'm/s', xTitle: 'τμήμα (μ.)',
      series: [{ name: 'Αυτός ο αγώνας', color: C.race, values: sp.map(s => s.v) }, { name: PB_NAME, color: C.pb, dash: 'dash', marker: 'square', values: spb.map(s => s.v) },
        { name: REF_NAME, color: C.ref, dash: 'dot', marker: 'diamond', values: spr.map(s => s.v) }] })}</div>`;
    const h1 = a.blocks[0].time, h2 = a.blocks[1].time;
    const free = a.rows.filter(r => r.isFree);
    const f1 = free.filter(r => r.d < 50), f2 = free.filter(r => r.d > 50);
    const vm = list => list.reduce((p, q) => p + (q.d - q.prevD), 0) / list.reduce((p, q) => p + q.seg, 0);
    const drop = (vm(f1) - vm(f2)) / vm(f1);
    body += section('Τα δύο 50άρια', 'Ο χρόνος κάθε 50άρι και η διαφορά τους: πόσο κράτησε ο αθλητής την ταχύτητα στο 2ο 50άρι.');
    body += `<div class="tiles t4">
      ${tile('1ο 50άρι', T(h1), ' s', h1 - b.blocks[0].time, h1 - c.blocks[0].time, 'lower')}
      ${tile('2ο 50άρι', T(h2), ' s', h2 - b.blocks[1].time, h2 - c.blocks[1].time, 'lower')}
      ${tile('Διαφορά 2ου − 1ου', S(h2 - h1), ' s', (h2 - h1) - (b.blocks[1].time - b.blocks[0].time), (h2 - h1) - (c.blocks[1].time - c.blocks[0].time), 'lower')}
      ${tile('Πτώση ταχύτητας', (drop * 100).toFixed(1), '%', null, null, 'none', 1, `${F(vm(f1))} → ${F(vm(f2))} m/s (15–45 → 65–95 μ.)`)}
    </div>`;
    const q = [25, 50, 75, 100];
    body += section('Ανά 25άρι', 'Ο χρόνος κάθε 25άρι: το 1ο έχει την εκκίνηση, το 3ο την έξοδο από τη στροφή.');
    body += `<div class="box"><table class="tbl"><thead><tr><th>25άρι</th><th>Χρόνος</th><th>Σωρευτικός</th><th>Ταχύτητα m/s</th><th>ΑΡ</th><th>Δ ΑΡ</th><th>Αναφ.</th><th>Δ Αναφ.</th></tr></thead><tbody>
      ${q.map((d, i) => { const t = r => r.byD[d].t - (i === 0 ? 0 : r.byD[q[i - 1]].t); return `<tr><td class="l">${d - 25}–${d} μ.</td><td><b>${T(t(a))}</b></td><td>${T(a.byD[d].t)}</td><td>${F(25 / t(a))}</td><td>${T(t(b))}</td><td>${chip(t(a) - t(b), 'lower')}</td><td>${T(t(c))}</td><td>${chip(t(a) - t(c), 'lower')}</td></tr>`; }).join('')}
      </tbody></table></div>`;
    body += cumChart(x, 200);
  } else if (a.p.style === 'im') {
    const cats = a.blocks.map(bk => bk.to + ' ' + ({ fly: 'πετ.', bk: 'ύπτ.', br: 'πρόσθ.', fs: 'ελ.' })[bk.style]);
    body += section('Ρυθμός ανά 50άρι', 'Ο χρόνος κάθε 50άρι της μικτής, δίπλα στο ΑΡ και την αναφορά.');
    body += `<div class="box">${ch.lineChart({ w: W - 24, h: 235, cats, fmt: v => T(v), yTitle: 's / 50 μ.', xTitle: '50άρι (μ.) · στυλ',
      series: [{ name: 'Αυτός ο αγώνας', color: C.race, values: a.blocks.map(bk => bk.time) },
        { name: PB_NAME, color: C.pb, dash: 'dash', marker: 'square', values: b.blocks.map(bk => bk.time) },
        { name: REF_NAME, color: C.ref, dash: 'dot', marker: 'diamond', values: c.blocks.map(bk => bk.time) }] })}</div>`;
    body += section('Διαφορά από το ΑΡ ανά 50άρι', 'Κάτω από το μηδέν πιο γρήγορα από το ΑΡ (μπλε), πάνω πιο αργά (πορτοκαλί).');
    body += `<div class="box">${ch.vDiverging({ w: W - 24, h: 140, cats, values: a.blocks.map((bk, i) => bk.time - b.blocks[i].time), labels: true, yTitle: 's' })}</div>`;
    const fade = r => IM_ORDER.map((st, i) => r.blocks[2 * i + 1].time - r.blocks[2 * i].time);
    const fa = fade(a), fb = fade(b), fc = fade(c);
    body += section('Ρυθμός μέσα σε κάθε στυλ', 'Πόσο πιο αργό είναι το 2ο 50άρι κάθε στυλ από το 1ο (στην πεταλούδα το 1ο έχει την εκκίνηση).');
    body += `<div class="tiles">${IM_ORDER.map((st, i) => tile(STYLE_NAME[st], S(fa[i]), ' s', fa[i] - fb[i], fa[i] - fc[i], 'lower', 2,
      `2ο − 1ο 50άρι: ${T(a.blocks[2 * i].time)} → ${T(a.blocks[2 * i + 1].time)}`)).join('')}</div>`;
    const per100 = IM_ORDER.map((st, i) => { const s0 = i * 100, e = s0 + 100, t = r => r.byD[e].t - (s0 === 0 ? 0 : r.byD[s0].t); return { s: s0, e, a: t(a), pb: t(b), ref: t(c), style: STYLE_NAME[st] }; });
    const per50 = a.blocks.map((bk, i) => ({ s: bk.from, e: bk.to, a: bk.time, pb: b.blocks[i].time, ref: c.blocks[i].time, style: STYLE_NAME[bk.style] }));
    const small = (title, list) => `<table class="tbl sm"><thead><tr><th>${esc(title)}</th><th>Χρόνος</th><th>Σωρευτ.</th><th>Δ ΑΡ</th><th>Δ Αναφ.</th></tr></thead><tbody>
      ${list.map(q => `<tr><td class="l">${q.s}–${q.e} · ${esc(q.style)}</td><td><b>${T(q.a)}</b></td><td>${T(a.byD[q.e].t)}</td><td>${chip(q.a - q.pb, 'lower')}</td><td>${chip(q.a - q.ref, 'lower')}</td></tr>`).join('')}</tbody></table>`;
    body += section('Χρόνοι ανά 50 και ανά 100 μ.', '');
    body += `<div class="two"><div class="box">${small('50άρι', per50)}</div><div class="box">${small('100άρι', per100)}</div></div>`;
  } else {
    const cats = a.blocks.map(bk => String(bk.to));
    const mean = a.blocks.slice(1).reduce((p, q) => p + q.time, 0) / (a.blocks.length - 1);
    body += section('Ρυθμός ανά 50άρι', `Ο χρόνος κάθε 50άρι. Η διακεκομμένη γραμμή είναι ο μέσος όρος από το 2ο 50άρι (${T(mean)} s)· το 1ο έχει την εκκίνηση.`);
    body += `<div class="box">${ch.lineChart({ w: W - 24, h: D >= 800 ? 225 : 235, cats, fmt: v => T(v), yTitle: 's / 50 μ.', every: D >= 1500 ? 2 : 1, xTitle: '50άρι (μ.)',
      refLine: { value: mean, label: 'μ.ο.' },
      series: [{ name: 'Αυτός ο αγώνας', color: C.race, values: a.blocks.map(bk => bk.time) },
        { name: PB_NAME, color: C.pb, dash: 'dash', marker: 'square', values: b.blocks.map(bk => bk.time) },
        { name: REF_NAME, color: C.ref, dash: 'dot', marker: 'diamond', values: c.blocks.map(bk => bk.time) }] })}</div>`;
    const dev = a.blocks.map((bk, i) => i === 0 ? null : bk.time - mean);
    body += section('Απόκλιση από τον μέσο όρο', 'Κάθε 50άρι σε σχέση με τον μέσο όρο: κάτω από το μηδέν πιο γρήγορα (μπλε), πάνω πιο αργά (πορτοκαλί).');
    body += `<div class="box">${ch.vDiverging({ w: W - 24, h: 140, cats, values: dev, labels: D <= 400, every: D >= 1500 ? 2 : 1, yTitle: 's' })}</div>`;
    body += paceSummary(x, mean);
  }
  return `<div class="page">${head(key, 'Ρυθμός αγώνα')}${body}${foot(n, N)}</div>`;
}

function paceType(a) {
  const D = a.p.distance, n = a.blocks.length;
  const h1 = a.blocks.slice(0, n / 2).reduce((p, q) => p + q.time, 0), h2 = a.blocks.slice(n / 2).reduce((p, q) => p + q.time, 0);
  const diff = h2 - h1;
  const rel = diff / (h1 + h2);
  if (a.p.style === 'im') return { name: 'Μικτή: ο ρυθμός ακολουθεί τα στυλ', diff, h1, h2 };
  const mid = a.blocks.slice(1, -1).map(b => b.time), last = a.blocks[n - 1].time, meanMid = mid.reduce((p, q) => p + q, 0) / mid.length;
  let name;
  if (rel < -0.002) name = 'Αρνητικός (πιο γρήγορο 2ο μισό)';
  else if (rel < 0.012) name = last < meanMid - 0.3 ? 'Σταθερός με δυνατό τέλος' : 'Σταθερός';
  else name = 'Θετικός (πιο αργό 2ο μισό)';
  return { name, diff, h1, h2 };
}

function paceSummary(x, mean) {
  const a = x.race, b = x.pb, c = x.ref, D = a.p.distance;
  const pt = paceType(a), ptb = paceType(b), ptc = paceType(c);
  const mid = a.blocks.slice(1).map(bk => bk.time);
  const sd = Math.sqrt(mid.reduce((p, q) => p + (q - mean) * (q - mean), 0) / mid.length);
  const fast = a.blocks.slice(1).reduce((p, q) => q.time < p.time ? q : p), slow = a.blocks.slice(1).reduce((p, q) => q.time > p.time ? q : p);
  const last100 = D >= 200 ? a.blocks.slice(-2).reduce((p, q) => p + q.time, 0) : null;
  const stats = `<div class="tiles t4">
    ${tile('Ρυθμός αγώνα', `<span class="t-word">${esc(pt.name)}</span>`, '', null, null, 'none', 2, `2ο μισό ${S(pt.diff)} s (ΑΡ ${S(ptb.diff)}, αναφ. ${S(ptc.diff)})`)}
    ${tile('Διακύμανση 50άρι', (100 * sd / mean).toFixed(1), '%', null, null, 'none', 1, `τυπική απόκλιση ${F(sd)} s`)}
    ${tile('Πιο γρήγορο / πιο αργό', `${T(fast.time)} / ${T(slow.time)}`, '', null, null, 'none', 2, `στα ${fast.to} και ${slow.to} μ. (χωρίς το 1ο)`)}
    ${last100 !== null ? tile('Τελευταία 100 μ.', T(last100), ' s', last100 - b.blocks.slice(-2).reduce((p, q) => p + q.time, 0), last100 - c.blocks.slice(-2).reduce((p, q) => p + q.time, 0), 'lower', 2, `μ.ο. 100άρι ${T(mean * 2)} s`) : ''}
  </div>`;
  // Splits per 100 (and per part for 800 / 1500).
  const per = D <= 400 ? 100 : D <= 800 ? 200 : 500;
  const parts = [];
  for (let s = 0; s < D; s += per) {
    const e = s + per;
    const t = r => r.byD[e].t - (s === 0 ? 0 : r.byD[s].t);
    parts.push({ s, e, a: t(a), pb: t(b), ref: t(c) });
  }
  const per100 = [];
  for (let s = 0; s < D; s += 100) {
    const e = s + 100, t = r => r.byD[e].t - (s === 0 ? 0 : r.byD[s].t);
    per100.push({ s, e, a: t(a), pb: t(b), ref: t(c), cum: a.byD[e].t, style: a.p.style === 'im' ? STYLE_NAME[IM_ORDER[s / 100]] : '' });
  }
  const tbl = (title, list, label) => `<table class="tbl"><thead><tr><th>${esc(title)}</th><th>Χρόνος</th><th>Σωρευτικός</th><th>ΑΡ</th><th>Δ ΑΡ</th><th>Αναφ.</th><th>Δ Αναφ.</th></tr></thead><tbody>
    ${list.map(p => `<tr><td class="l">${label(p)}</td><td><b>${T(p.a)}</b></td><td>${T(a.byD[p.e].t)}</td><td>${T(p.pb)}</td><td>${chip(p.a - p.pb, 'lower')}</td><td>${T(p.ref)}</td><td>${chip(p.a - p.ref, 'lower')}</td></tr>`).join('')}
    </tbody></table>`;
  const small = (title, list, label) => `<table class="tbl sm"><thead><tr><th>${esc(title)}</th><th>Χρόνος</th><th>Σωρευτ.</th><th>Δ ΑΡ</th><th>Δ Αναφ.</th></tr></thead><tbody>
    ${list.map(p => `<tr><td class="l">${label(p)}</td><td><b>${T(p.a)}</b></td><td>${T(a.byD[p.e].t)}</td><td>${chip(p.a - p.pb, 'lower')}</td><td>${chip(p.a - p.ref, 'lower')}</td></tr>`).join('')}
    </tbody></table>`;
  const per50 = a.blocks.map((bk, i) => ({ s: bk.from, e: bk.to, a: bk.time, pb: b.blocks[i].time, ref: c.blocks[i].time, style: a.p.style === 'im' ? STYLE_NAME[bk.style] : '' }));
  let tables;
  if (D <= 400) tables = `<div class="two"><div class="box">${small('50άρι', per50, p => `${p.s}–${p.e}${p.style ? ' · ' + p.style : ''}`)}</div>
    <div class="box">${small('100άρι', per100, p => `${p.s}–${p.e}${p.style ? ' · ' + p.style : ''}`)}</div></div>`;
  else tables = `<div class="two"><div class="box">${small(per === 200 ? '200άρι' : '500άρι', parts, p => `${p.s}–${p.e} μ.`)}</div>
    <div class="box">${small('100άρι', per100, p => `${p.s}–${p.e}`)}</div></div>`;
  return `${section('Σύνοψη ρυθμού', 'Πώς μοιράστηκε ο χρόνος στον αγώνα.')}${stats}${section(D <= 400 ? 'Χρόνοι ανά 50 και ανά 100 μ.' : 'Χρόνοι ανά μέρος του αγώνα και ανά 100 μ.', '')}${tables}`;
}

// ---- checkpoint table
function checkpointTable(x, limit, compact) {
  const a = x.race, b = x.pb, c = x.ref;
  const rows = a.rows.slice(0, limit);
  return `<div class="box"><table class="tbl${compact ? ' sm' : ''}"><thead><tr><th>Πέρασμα</th><th>Χρόνος</th><th>Τμήμα</th><th>Ταχύτητα<br>m/s</th><th>Χεριές</th><th>Συχνότητα<br>κύκλ./λ.</th><th>Tempo<br>s/κύκλο</th><th>Μήκος κύκλου<br>μ.</th><th>Δ ΑΡ</th><th>Δ Αναφ.</th></tr></thead><tbody>
  ${rows.map(r => `<tr class="${r.isFree ? '' : 'phase'}"><td class="l"><b>${r.d}</b> μ.</td><td><b>${T(r.t)}</b></td><td>${F(r.seg)}</td><td>${F(r.vel)}</td><td>${r.isFree ? F(r.strokes, 1) : '<span class="dim">' + F(r.strokes, 1) + '</span>'}</td>
    <td>${r.sr ? F(r.sr, 1) : ''}</td><td>${r.tempo ? F(r.tempo) : ''}</td><td>${r.sl ? F(r.sl) : ''}</td><td>${chip(r.t - b.byD[r.d].t, 'lower')}</td><td>${chip(r.t - c.byD[r.d].t, 'lower')}</td></tr>`).join('')}
  </tbody></table></div>`;
}

// ---- page: strokes
function pageStrokes(x, key, n, N) {
  const a = x.race, b = x.pb, c = x.ref, D = a.p.distance;
  let body = '';
  const three = (cats, get, opts) => {
    const mk = (title, f, fmt, yTitle) => `<div class="box">${ch.lineChart({ w: W - 24, h: opts.h || 170, cats, fmt, yTitle, every: opts.every,
      series: get(f) })}</div>`;
    return section(opts.t1, opts.s1) + mk('v', 'vel', v => v.toFixed(2), 'Ταχύτητα (m/s)') + mk('sr', 'sr', v => v.toFixed(1), 'Συχνότητα (κύκλοι/λεπτό)') +
      mk('sl', 'sl', v => v.toFixed(2), 'Μήκος κύκλου (μ.)');
  };
  if (D <= 200) {
    const free = r => r.rows.filter(q => q.isFree);
    const cats = free(a).map(r => r.prevD + '–' + r.d);
    const get = f => [{ name: 'Αυτός ο αγώνας', color: C.race, values: free(a).map(r => r[f]) },
      { name: PB_NAME, color: C.pb, dash: 'dash', marker: 'square', values: free(b).map(r => r[f]) },
      { name: REF_NAME, color: C.ref, dash: 'dot', marker: 'diamond', values: free(c).map(r => r[f]) }];
    body += three(cats, get, { t1: 'Ταχύτητα, συχνότητα και μήκος κύκλου', s1: 'Μόνο στην καθαρή κολύμβηση (15 μ. μετά τον τοίχο ως 5 μ. πριν τον επόμενο): ταχύτητα = συχνότητα × μήκος κύκλου.', h: D === 200 ? 160 : 175 });
    body += section('Οι χεριές ανά τμήμα', '');
    body += strokeTable(x);
  } else if (D === 400) {
    const cats = a.blocks.map(bk => String(bk.to));
    const val = (r, f, which) => r.blocks.map(bk => bk[which] ? bk[which][f] : null);
    const pair = f => [{ name: '1ο 25άρι (15–25 μ.)', label: '1ο 25', color: C.first25, values: val(a, f, 'first25') },
      { name: '2ο 25άρι (25–45 μ.)', label: '2ο 25', color: C.second25, marker: 'square', values: val(a, f, 'second25') }];
    body += section('Χεριές ανά 25άρι', 'Σε κάθε 50άρι: 1ο 25άρι = 15→25 μ., 2ο 25άρι = 25→45 μ. (καθαρή κολύμβηση). Ταχύτητα = συχνότητα × μήκος κύκλου.');
    const mk = (f, fmt, yTitle) => `<div class="box">${ch.lineChart({ w: W - 24, h: 165, cats, fmt, yTitle, series: pair(f), xTitle: '50άρι (μ.)' })}</div>`;
    body += mk('vel', v => v.toFixed(2), 'Ταχύτητα (m/s)') + mk('sr', v => v.toFixed(1), 'Συχνότητα (κύκλοι/λεπτό)') + mk('sl', v => v.toFixed(2), 'Μήκος κύκλου (μ.)');
    body += section('Ανά 50άρι: 1ο και 2ο 25άρι', '');
    body += `<div class="box"><table class="tbl sm"><thead><tr><th rowspan="2">50άρι</th>${a.p.style === 'im' ? '<th rowspan="2">Στυλ</th>' : ''}<th rowspan="2">Χρόνος</th><th colspan="4" class="g1">1ο 25άρι · 15→25 μ.</th><th colspan="4" class="g2">2ο 25άρι · 25→45 μ.</th><th rowspan="2">ΔΑΚ</th><th rowspan="2">Swolf</th></tr>
      <tr><th class="g1">m/s</th><th class="g1">κύκλ./λ.</th><th class="g1">tempo</th><th class="g1">μήκος</th><th class="g2">m/s</th><th class="g2">κύκλ./λ.</th><th class="g2">tempo</th><th class="g2">μήκος</th></tr></thead><tbody>
      ${a.blocks.map(bk => { const f = bk.first25, s = bk.second25; return `<tr><td class="l">${bk.from}–${bk.to}</td>${a.p.style === 'im' ? `<td>${esc(STYLE_NAME[bk.style])}</td>` : ''}<td><b>${T(bk.time)}</b></td>
        <td>${F(f.vel)}</td><td>${F(f.sr, 1)}</td><td>${F(f.tempo)}</td><td>${F(f.sl)}</td><td>${F(s.vel)}</td><td>${F(s.sr, 1)}</td><td>${F(s.tempo)}</td><td>${F(s.sl)}</td><td>${F(bk.dak)}</td><td>${F(bk.swolf, 1)}</td></tr>`; }).join('')}
      </tbody></table></div>`;
    body += quarterInsight(a);
  } else {
    const cats = a.blocks.map(bk => String(bk.to));
    const get = f => [{ name: 'Αυτός ο αγώνας', color: C.race, values: a.blocks.map(bk => bk[f]) },
      { name: PB_NAME, color: C.pb, dash: 'dash', marker: 'square', values: b.blocks.map(bk => bk[f]) },
      { name: REF_NAME, color: C.ref, dash: 'dot', marker: 'diamond', values: c.blocks.map(bk => bk[f]) }];
    const mk = (f, fmt, yTitle) => `<div class="box">${ch.lineChart({ w: W - 24, h: 190, cats, fmt, yTitle, series: get(f), every: D >= 1500 ? 2 : 1, xTitle: '50άρι (μ.)' })}</div>`;
    body += section('Χεριές ανά 50άρι', 'Στην καθαρή κολύμβηση κάθε 50άρι (15→45 μ.). Ταχύτητα = συχνότητα × μήκος κύκλου.');
    body += mk('vClean', v => v.toFixed(2), 'Ταχύτητα 15–45 μ. (m/s)') + mk('sr', v => v.toFixed(1), 'Συχνότητα (κύκλοι/λεπτό)') + mk('sl', v => v.toFixed(2), 'Μήκος κύκλου (μ.)');
    // Summary per part.
    const per = D <= 800 ? 200 : 500;
    const parts = [];
    for (let s = 0; s < D; s += per) {
      const bl = a.blocks.filter(bk => bk.from >= s && bk.to <= s + per);
      const w = f => bl.reduce((p, q) => p + q[f], 0) / bl.length;
      parts.push({ s, e: s + per, v: w('vClean'), sr: w('sr'), tempo: w('tempo'), sl: w('sl'), si: w('si'), dak: w('dak') });
    }
    body += strokeInsight50(a);
    body += section(`Μέσοι όροι ανά ${per} μ.`, '');
    body += `<div class="box"><table class="tbl"><thead><tr><th>Μέρος</th><th>Ταχύτητα m/s</th><th>Συχνότητα κύκλ./λ.</th><th>Tempo s/κύκλο</th><th>Μήκος κύκλου μ.</th><th>SI m²/s</th><th>ΔΑΚ</th></tr></thead><tbody>
      ${parts.map(p => `<tr><td class="l">${p.s}–${p.e} μ.</td><td><b>${F(p.v)}</b></td><td>${F(p.sr, 1)}</td><td>${F(p.tempo)}</td><td>${F(p.sl)}</td><td>${F(p.si)}</td><td>${F(p.dak)}</td></tr>`).join('')}
      </tbody></table></div>`;
  }
  return `<div class="page">${head(key, 'Χεριές: συχνότητα, tempo, μήκος κύκλου')}${body}${foot(n, N)}</div>`;
}

function strokeInsight50(a) {
  const bl = a.blocks, mid = bl.slice(1, -2), last = bl.slice(-2);
  const rng = f => [Math.min(...mid.map(b => b[f])), Math.max(...mid.map(b => b[f]))];
  const m = (list, f) => list.reduce((p, b) => p + b[f], 0) / list.length;
  const sr = rng('sr'), sl = rng('sl');
  const items = [
    `Από τα ${mid[0].from} ως τα ${mid[mid.length - 1].to} μ.: συχνότητα <b>${F(sr[0], 1)}–${F(sr[1], 1)}</b> κύκλοι/λεπτό, μήκος κύκλου <b>${F(sl[0])}–${F(sl[1])}</b> μ., tempo ${F(60 / sr[1])}–${F(60 / sr[0])} s/κύκλο.`,
    `Στο τελευταίο 100άρι η συχνότητα ανεβαίνει σε <b>${F(m(last, 'sr'), 1)}</b> (${S(m(last, 'sr') - m(mid, 'sr'), 1)}) και το μήκος κύκλου γίνεται ${F(m(last, 'sl'))} μ. (${S(m(last, 'sl') - m(mid, 'sl'))}): ταχύτητα ${F(m(last, 'vClean'))} m/s έναντι ${F(m(mid, 'vClean'))}.`
  ];
  return `<div class="ins"><div class="ins-t">Τι δείχνουν τα 50άρια</div><ul>${items.map(t => `<li>${t}</li>`).join('')}</ul></div>`;
}

function quarterInsight(a) {
  const pairs = a.blocks.filter(bk => bk.first25 && bk.second25);
  const m = f => pairs.reduce((p, bk) => p + (bk.second25[f] - bk.first25[f]), 0) / pairs.length;
  const mv = pairs.reduce((p, bk) => p + (bk.second25.vel - bk.first25.vel) / bk.first25.vel, 0) / pairs.length;
  const big = pairs.reduce((p, bk) => (bk.second25.vel - bk.first25.vel) < (p.second25.vel - p.first25.vel) ? bk : p);
  const lastB = pairs[pairs.length - 1];
  const mid = pairs.slice(1, -1);
  const srMid = mid.reduce((p, bk) => p + (bk.first25.sr + bk.second25.sr) / 2, 0) / mid.length;
  const im = a.p.style === 'im';
  const items = [
    `Το 2ο 25άρι κάθε 50άρι είναι κατά μ.ο. <b>${S(m('vel'))} m/s</b> (${(mv * 100).toFixed(1).replace('-', '−')}%) σε σχέση με το 1ο: συχνότητα ${S(m('sr'), 1)} κύκλοι/λεπτό, μήκος κύκλου ${S(m('sl'))} μ.`,
    `Η μεγαλύτερη διαφορά στο 50άρι <b>${big.from}–${big.to} μ.</b>: ${F(big.first25.vel)} → ${F(big.second25.vel)} m/s.`,
    im ? `Ανά στυλ η συχνότητα και το μήκος κύκλου συγκρίνονται μόνο μέσα στο ίδιο στυλ (δύο 50άρια το καθένα).`
      : `Στο τελευταίο 50άρι η συχνότητα ανεβαίνει σε <b>${F((lastB.first25.sr + lastB.second25.sr) / 2, 1)}</b> κύκλους/λεπτό (μ.ο. ${F(srMid, 1)} στα ενδιάμεσα 50άρια).`
  ];
  return `<div class="ins"><div class="ins-t">Τι δείχνουν τα 25άρια</div><ul>${items.map(t => `<li>${t}</li>`).join('')}</ul></div>`;
}

// ---- page: per-50 table (800 / 1500 / 400s)
function pagePer50(x, key, n, N) {
  const a = x.race, b = x.pb, c = x.ref, D = a.p.distance;
  const mean = a.blocks.slice(1).reduce((p, q) => p + q.time, 0) / (a.blocks.length - 1);
  const im = a.p.style === 'im';
  const body = section('Πίνακας ανά 50άρι', `Χρόνος, ρυθμός και χεριές κάθε 50άρι. Δ μ.ο.: διαφορά από τον μέσο όρο ${T(mean)} s (από το 2ο 50άρι).`) +
    `<div class="box"><table class="tbl ${D >= 1500 ? 'xs' : 'sm'}"><thead><tr><th>50άρι</th>${im ? '<th>Στυλ</th>' : ''}<th>Χρόνος</th><th>Σωρευτικός</th><th>Δ μ.ο.</th><th>Δ ΑΡ</th><th>Δ Αναφ.</th><th>Ταχύτητα<br>καθαρή</th><th>Χεριές</th><th>Συχνότητα</th><th>Tempo</th><th>Μήκος<br>κύκλου</th><th>SI</th><th>ΔΑΚ</th><th>Swolf</th></tr></thead><tbody>
    ${a.blocks.map((bk, i) => `<tr><td class="l">${bk.from}–${bk.to}</td>${im ? `<td>${esc(STYLE_NAME[bk.style])}</td>` : ''}<td><b>${T(bk.time)}</b></td><td>${T(bk.cum)}</td><td>${i === 0 ? '' : chip(bk.time - mean, 'lower')}</td><td>${chip(bk.time - b.blocks[i].time, 'lower')}</td><td>${chip(bk.time - c.blocks[i].time, 'lower')}</td>
      <td>${F(bk.vClean)}</td><td>${F(bk.strokes, 1)}</td><td>${F(bk.sr, 1)}</td><td>${F(bk.tempo)}</td><td>${F(bk.sl)}</td><td>${F(bk.si)}</td><td>${F(bk.dak)}</td><td>${F(bk.swolf, 1)}</td></tr>`).join('')}
    <tr class="tot"><td class="l">Σύνολο / μ.ο.</td>${im ? '<td></td>' : ''}<td><b>${T(a.final)}</b></td><td></td><td></td><td>${chip(a.final - b.final, 'lower')}</td><td>${chip(a.final - c.final, 'lower')}</td><td>${F(a.avg.vClean)}</td><td>${F(a.blocks.reduce((p, q) => p + q.strokes, 0), 1)}</td><td>${F(a.avg.sr, 1)}</td><td>${F(a.avg.tempo)}</td><td>${F(a.avg.sl)}</td><td>${F(a.avg.si)}</td><td>${F(a.avg.dak)}</td><td>${F(a.blocks.reduce((p, q) => p + q.swolf, 0) / a.blocks.length, 1)}</td></tr>
    </tbody></table></div>`;
  const cats = a.blocks.map(bk => String(bk.to));
  const dak = section('ΔΑΚ ανά 50άρι', 'Χεριές και δευτερόλεπτα από τα 15 μ. ως τον τοίχο, ανά 10 μ.: όσο μικρότερος, τόσο πιο αποδοτικό το κολύμπι.') +
    `<div class="box">${ch.lineChart({ w: W - 24, h: D >= 1500 ? 200 : 215, cats, fmt: v => v.toFixed(2), yTitle: 'ΔΑΚ', every: D >= 1500 ? 2 : 1, xTitle: '50άρι (μ.)',
      series: [{ name: 'Αυτός ο αγώνας', color: C.race, values: a.blocks.map(bk => bk.dak) }, { name: PB_NAME, color: C.pb, dash: 'dash', marker: 'square', values: b.blocks.map(bk => bk.dak) },
        { name: REF_NAME, color: C.ref, dash: 'dot', marker: 'diamond', values: c.blocks.map(bk => bk.dak) }] })}</div>`;
  return `<div class="page">${head(key, 'Ανά 50άρι')}${body}${dak}${foot(n, N)}</div>`;
}

// ---- page: start, turns, finish
function startTable(x) {
  const a = x.race.start, b = x.pb.start, c = x.ref.start;
  const rows = [
    ['Αντίδραση (πρώτη κίνηση)', 'reaction', 's', 'lower', 2],
    ['Χρόνος στον βατήρα', 'block', 's', 'lower', 2],
    ['Πτήση: βατήρας → είσοδος', 'flight', 's', 'none', 2],
    ['Χρόνος ως την είσοδο', 'entryT', 's', 'lower', 2],
    ['Απόσταση εισόδου (χέρια)', 'entryDist', 'μ.', 'higher', 2],
    ['Ταχύτητα εισόδου (χέρια)', 'entryVel', 'm/s', 'higher', 2],
    ['Υποβρύχιοι δελφινισμοί', 'kicks', '', 'none', 0],
    ['Χρόνος ως την έξοδο', 'bt', 's', 'none', 2],
    ['Απόσταση εξόδου (κεφάλι)', 'bd', 'μ.', 'higher', 2],
    ['Ταχύτητα είσοδος → έξοδος', 'entryBoVel', 'm/s', 'higher', 2],
    ['Ταχύτητα έξοδος → 15 μ.', 'bo15Vel', 'm/s', 'higher', 2],
    ['Χρόνος 15 μ.', 't15', 's', 'lower', 2]
  ];
  return `<table class="tbl"><thead><tr><th>Εκκίνηση</th><th>Αυτός ο αγώνας</th><th>ΑΡ</th><th>Αναφορά</th><th>Δ ΑΡ</th><th>Δ Αναφ.</th></tr></thead><tbody>
    ${rows.map(([lab, k, u, better, d]) => `<tr${k === 't15' ? ' class="tot"' : ''}><td class="l">${esc(lab)}</td><td><b>${F(a[k], d)}</b> <span class="dim">${esc(u)}</span></td><td>${F(b[k], d)}</td><td>${F(c[k], d)}</td><td>${chip(a[k] - b[k], better, d)}</td><td>${chip(a[k] - c[k], better, d)}</td></tr>`).join('')}
  </tbody></table>`;
}

function turnsTable(x, compact) {
  const a = x.race, b = x.pb, c = x.ref;
  const im = a.p.style === 'im';
  return `<table class="tbl ${compact || ''}"><thead><tr><th>Τοίχος</th>${im ? '<th>Είδος</th>' : ''}<th>−5 → τοίχος</th><th>Στροφή</th><th>Υποβρύχια</th><th>Δελφ.</th><th>Έξοδος<br>μ.</th><th>Ταχ. υποβρ.<br>m/s</th><th>Τοίχος → +15</th><th>Ταχ. έξοδος<br>→ +15</th><th>−5/+15</th><th>Δ ΑΡ</th><th>Δ Αναφ.</th></tr></thead><tbody>
    ${a.turns.map((t, i) => `<tr><td class="l"><b>${t.wall}</b> μ.</td>${im ? `<td class="l sm">${esc(t.kind)}</td>` : ''}<td>${F(t.in5)}</td><td>${F(t.turnTime)}</td><td>${F(t.uw)}</td><td>${t.kicks}</td><td>${F(t.bd, 1)}</td><td>${F(t.vUw)}</td><td>${F(t.out15)}</td><td>${F(t.vBo15)}</td><td><b>${F(t.inOut)}</b></td><td>${chip(t.inOut - b.turns[i].inOut, 'lower')}</td><td>${chip(t.inOut - c.turns[i].inOut, 'lower')}</td></tr>`).join('')}
    <tr class="tot"><td class="l">Μ.ο.</td>${im ? '<td></td>' : ''}<td>${F(avg(a.turns, 'in5'))}</td><td>${F(avg(a.turns, 'turnTime'))}</td><td>${F(avg(a.turns, 'uw'))}</td><td>${F(avg(a.turns, 'kicks'), 1)}</td><td>${F(avg(a.turns, 'bd'), 1)}</td><td>${F(avg(a.turns, 'vUw'))}</td><td>${F(avg(a.turns, 'out15'))}</td><td>${F(avg(a.turns, 'vBo15'))}</td><td><b>${F(avg(a.turns, 'inOut'))}</b></td><td>${chip(avg(a.turns, 'inOut') - avg(b.turns, 'inOut'), 'lower')}</td><td>${chip(avg(a.turns, 'inOut') - avg(c.turns, 'inOut'), 'lower')}</td></tr>
  </tbody></table>`;
}
const avg = (list, k) => list.reduce((p, q) => p + q[k], 0) / list.length;

function finishTiles(x) {
  const a = x.race.finish, b = x.pb.finish, c = x.ref.finish;
  return `<div class="tiles t4">
    ${tile('Τελευταία 5 μ.', T(a.last5), ' s', a.last5 - b.last5, a.last5 - c.last5, 'lower')}
    ${tile('Τελευταία χεριά → επαφή', F(a.lastGap), ' s', a.lastGap - b.lastGap, a.lastGap - c.lastGap, 'lower')}
    ${tile('Ταχύτητα τελευταίων 5 μ.', F(a.vLast5), ' m/s', a.vLast5 - b.vLast5, a.vLast5 - c.vLast5, 'higher')}
    ${tile(`Τελευταία ${a.lastX} μ.`, T(a.lastXt), ' s', a.lastXt - b.lastXt, a.lastXt - c.lastXt, 'lower')}
  </div>`;
}

function lastHundred(x) {
  const a = x.race, b = x.pb, c = x.ref;
  const mean = a.blocks.slice(1).reduce((p, q) => p + q.time, 0) / (a.blocks.length - 1);
  const last = a.blocks.slice(-2);
  const mid = a.blocks.slice(1, -2);
  const w = f => mid.reduce((p, q) => p + q[f], 0) / mid.length;
  return `<div class="box"><table class="tbl"><thead><tr><th>Τελευταίο 100άρι</th><th>Χρόνος</th><th>Δ μ.ο.</th><th>Δ ΑΡ</th><th>Δ Αναφ.</th><th>Ταχύτητα m/s</th><th>Συχνότητα</th><th>Tempo</th><th>Μήκος κύκλου</th></tr></thead><tbody>
    ${last.map((bk, k) => { const i = a.blocks.length - 2 + k; return `<tr><td class="l">${bk.from}–${bk.to} μ.</td><td><b>${T(bk.time)}</b></td><td>${chip(bk.time - mean, 'lower')}</td><td>${chip(bk.time - b.blocks[i].time, 'lower')}</td><td>${chip(bk.time - c.blocks[i].time, 'lower')}</td>
      <td>${F(bk.vClean)}</td><td>${F(bk.sr, 1)}</td><td>${F(bk.tempo)}</td><td>${F(bk.sl)}</td></tr>`; }).join('')}
    <tr class="tot"><td class="l">Μ.ο. ${mid.length > 1 ? `${mid[0].from}–${mid[mid.length - 1].to} μ.` : ''}</td><td>${T(w('time'))}</td><td></td><td></td><td></td><td>${F(w('vClean'))}</td><td>${F(w('sr'), 1)}</td><td>${F(w('tempo'))}</td><td>${F(w('sl'))}</td></tr>
  </tbody></table></div>`;
}

function pageStart(x, key, n, N) {
  const a = x.race, D = a.p.distance;
  let body = section('Εκκίνηση', 'Από το σήμα ως τα 15 μ.: χρόνοι από το σήμα, αποστάσεις από τον τοίχο της εκκίνησης.');
  body += `<div class="box">${ch.startStrip({ w: W - 24, s: a.start, fmt: v => F(v) })}</div>`;
  body += `<div class="box">${startTable(x)}</div>`;
  const sp = st => [
    { name: 'Βατήρας', value: st.block, color: '#123B8A' },
    { name: 'Πτήση', value: st.flight, color: '#2E6BD6' },
    { name: 'Υποβρύχια', value: st.bt - st.entryT, color: '#5A8EDC' },
    { name: 'Ως τα 15 μ.', value: st.t15 - st.bt, color: '#A9C4EE', ink: '#123B8A' }];
  if (D !== 100) body += section('Η εκκίνηση σε φάσεις', 'Ο χρόνος ως τα 15 μ. μοιρασμένος στις φάσεις της εκκίνησης, δίπλα στο ΑΡ και την αναφορά.');
  if (D !== 100) body += `<div class="box">${ch.stacked({ w: W - 24, fmtT: v => F(v) + ' s', rows: [
    { label: 'Αυτός ο αγώνας', parts: sp(a.start), total: a.start.t15 }, { label: PB_NAME, parts: sp(x.pb.start), total: x.pb.start.t15 },
    { label: REF_NAME, parts: sp(x.ref.start), total: x.ref.start.t15 }] })}
    <div class="lgrow">${sp(a.start).map(p => `<span class="lgi"><i style="background:${p.color}"></i>${esc(p.name)}</span>`).join('')}</div></div>`;
  if (D === 100) {
    body += section('Στροφή στα 50 μ.', 'Από 5 μ. πριν ως 15 μ. μετά τον τοίχο.');
    const t = a.turns[0], tb = x.pb.turns[0], tc = x.ref.turns[0];
    body += `<div class="box">${ch.turnStrip({ w: W - 24, t, fmt: v => F(v) })}</div>`;
    body += `<div class="tiles t4">
      ${tile('−5/+15', F(t.inOut), ' s', t.inOut - tb.inOut, t.inOut - tc.inOut, 'lower')}
      ${tile('Στροφή', F(t.turnTime), ' s', t.turnTime - tb.turnTime, t.turnTime - tc.turnTime, 'lower', 2, 'τελευταία χεριά → πόδια φεύγουν')}
      ${tile('Απόσταση εξόδου', F(t.bd, 1), ' μ.', t.bd - tb.bd, t.bd - tc.bd, 'higher', 1, `${t.kicks} δελφινισμοί (ΑΡ ${tb.kicks}, αναφ. ${tc.kicks})`)}
      ${tile('Τοίχος → +15', F(t.out15), ' s', t.out15 - tb.out15, t.out15 - tc.out15, 'lower')}
    </div>`;
  }
  body += section('Τερματισμός', D >= 200 ? 'Τα τελευταία μέτρα ως την επαφή και το τελευταίο 100άρι σε σχέση με τον ρυθμό του αγώνα.' : 'Τα τελευταία μέτρα ως την επαφή.');
  body += finishTiles(x);
  if (D >= 200) body += lastHundred(x);
  return `<div class="page">${head(key, D === 100 ? 'Εκκίνηση, στροφή, τερματισμός' : 'Εκκίνηση και τερματισμός')}${body}${foot(n, N)}</div>`;
}

function pageTurns(x, key, n, N) {
  const a = x.race, b = x.pb, c = x.ref;
  let body = '';
  const nT = a.turns.length;
  body += section(`Στροφές (${nT})`, 'Κάθε στροφή από 5 μ. πριν ως 15 μ. μετά τον τοίχο, με τα γεγονότα στη σειρά που γίνονται. Στροφή: τελευταία χεριά → πόδια φεύγουν (τούμπα) ή επαφή → πόδια φεύγουν (ανοιχτή).');
  body += `<div class="box">${turnsTable(x, nT > 16 ? 'xs' : nT > 4 ? 'sm' : '')}</div>`;
  const cats = a.turns.map(t => String(t.wall));
  const ser = f => [{ name: 'Αυτός ο αγώνας', color: C.race, values: a.turns.map(t => t[f]) }, { name: PB_NAME, color: C.pb, dash: 'dash', marker: 'square', values: b.turns.map(t => t[f]) },
    { name: REF_NAME, color: C.ref, dash: 'dot', marker: 'diamond', values: c.turns.map(t => t[f]) }];
  const h = nT > 16 ? 165 : nT > 4 ? 185 : 200;
  body += section('Οι στροφές μέσα στον αγώνα', 'Πώς αλλάζουν από στροφή σε στροφή: ο χρόνος −5/+15 και η απόσταση εξόδου.');
  body += `<div class="box">${ch.lineChart({ w: W - 24, h, cats, fmt: v => v.toFixed(2), yTitle: '−5/+15 (s)', series: ser('inOut'), every: nT > 16 ? 2 : 1, xTitle: 'τοίχος (μ.)' })}</div>`;
  body += `<div class="box">${ch.lineChart({ w: W - 24, h, cats, fmt: v => v.toFixed(1), yTitle: 'Απόσταση εξόδου (μ.)', series: ser('bd'), every: nT > 16 ? 2 : 1, xTitle: 'τοίχος (μ.)' })}</div>`;
  if (a.p.style !== 'im' && nT <= 7) {
    const m = {};
    ['in5', 'out15', 'bd', 'turnTime', 'uw', 'vUw', 'vBo15', 'inOut'].forEach(k => { m[k] = avg(a.turns, k); });
    m.kicks = F(avg(a.turns, 'kicks'), 1);
    body += section('Η μέση στροφή του αγώνα', `Οι μέσοι όροι των ${nT} στροφών: −5/+15 σε ${F(m.inOut)} s (ΑΡ ${F(avg(b.turns, 'inOut'))}, αναφορά ${F(avg(c.turns, 'inOut'))}).`);
    body += `<div class="box">${ch.turnStrip({ w: W - 24, t: m, fmt: v => F(v) })}</div>`;
  }
  return `<div class="page">${head(key, 'Στροφές')}${body}${foot(n, N)}</div>`;
}

// ---- 400 IM: by style
function pageStyles(x, key, n, N) {
  const a = x.race, b = x.pb, c = x.ref;
  const legs = IM_ORDER.map((st, i) => {
    const s = i * 100, e = s + 100, t = r => r.byD[e].t - (s === 0 ? 0 : r.byD[s].t);
    const bl = r => r.blocks.filter(bk => bk.from >= s && bk.to <= e);
    const free = r => r.rows.filter(q => q.isFree && q.prevD >= s && q.d <= e);
    const w = (r, f) => { const list = free(r); let cyc = 0, ct = 0, cd = 0; list.forEach(q => { cyc += q.sr * q.seg / 60; ct += q.seg; cd += q.d - q.prevD; }); return f === 'v' ? cd / ct : f === 'sr' ? cyc * 60 / ct : cd / cyc; };
    return { st, s, e, a: t(a), pb: t(b), ref: t(c), v: w(a, 'v'), sr: w(a, 'sr'), sl: w(a, 'sl'), vPb: w(b, 'v'), vRef: w(c, 'v'),
      dak: bl(a).reduce((p, q) => p + q.dak, 0) / 2, h1: bl(a)[0].time, h2: bl(a)[1].time };
  });
  let body = section('Ανά στυλ', 'Ο χρόνος κάθε 100άρι της μικτής και η διαφορά του από το ΑΡ και την αναφορά.');
  body += `<div class="tiles">${legs.map(l => tile(STYLE_NAME[l.st], T(l.a), ' s', l.a - l.pb, l.a - l.ref, 'lower', 2, `50άρια ${T(l.h1)} · ${T(l.h2)}`)).join('')}</div>`;
  const share = r => IM_ORDER.map((st, i) => { const s0 = i * 100, e = s0 + 100; return { name: STYLE_NAME[st], value: r.byD[e].t - (s0 === 0 ? 0 : r.byD[s0].t),
    color: ['#123B8A', '#2E6BD6', '#5A8EDC', '#A9C4EE'][i], ink: i === 3 ? '#123B8A' : undefined }; });
  body += section('Πώς μοιράζεται ο χρόνος στα στυλ', 'Ο χρόνος κάθε 100άρι ως μέρος του τελικού χρόνου, δίπλα στο ΑΡ και την αναφορά.');
  body += `<div class="box">${ch.stacked({ w: W - 24, fmtT: v => T(v) + ' s', rows: [
    { label: 'Αυτός ο αγώνας', parts: share(a), total: a.final }, { label: PB_NAME, parts: share(b), total: b.final }, { label: REF_NAME, parts: share(c), total: c.final }] })}</div>`;
  body += section('Διαφορά από την αναφορά ανά στυλ', 'Πόσο χρόνο έχασε ή κέρδισε κάθε στυλ από την αναφορά (μπάρες) και από το ΑΡ (δεξιά).');
  body += `<div class="box tight">${ch.hDiverging({ w: W - 24, items: legs.map(l => ({ label: STYLE_NAME[l.st] + ` (${l.s}–${l.e})`, value: l.a - l.ref, note: `ΑΡ ${S(l.a - l.pb)}` })), labelW: 160, noteW: 92 })}</div>`;
  body += section('Κολύμβηση ανά στυλ', 'Στην καθαρή κολύμβηση κάθε στυλ.');
  body += `<div class="box"><table class="tbl"><thead><tr><th>Στυλ</th><th>Χρόνος 100</th><th>Ταχύτητα m/s</th><th>Δ ταχ. ΑΡ</th><th>Δ ταχ. Αναφ.</th><th>Συχνότητα κύκλ./λ.</th><th>Tempo s/κύκλο</th><th>Μήκος κύκλου μ.</th><th>ΔΑΚ</th></tr></thead><tbody>
    ${legs.map(l => `<tr><td class="l"><b>${esc(STYLE_NAME[l.st])}</b></td><td>${T(l.a)}</td><td><b>${F(l.v)}</b></td><td>${chip(l.v - l.vPb, 'higher')}</td><td>${chip(l.v - l.vRef, 'higher')}</td><td>${F(l.sr, 1)}</td><td>${F(60 / l.sr)}</td><td>${F(l.sl)}</td><td>${F(l.dak)}</td></tr>`).join('')}
  </tbody></table></div>`;
  const tr = a.turns.filter(t => t.styleIn !== t.styleOut);
  body += section('Αλλαγές στυλ', 'Οι τρεις στροφές αλλαγής στυλ: από 5 μ. πριν ως 15 μ. μετά τον τοίχο.');
  body += `<div class="box"><table class="tbl"><thead><tr><th>Τοίχος</th><th>Αλλαγή</th><th>−5 → τοίχος</th><th>Επαφή → πόδια φεύγουν</th><th>Υποβρύχια</th><th>Δελφ.</th><th>Έξοδος μ.</th><th>Τοίχος → +15</th><th>−5/+15</th><th>Δ ΑΡ</th><th>Δ Αναφ.</th></tr></thead><tbody>
    ${tr.map(t => { const i = a.turns.indexOf(t); return `<tr><td class="l"><b>${t.wall}</b> μ.</td><td class="l">${esc(t.kind)}</td><td>${F(t.in5)}</td><td>${F(t.turnTime)}</td><td>${F(t.uw)}</td><td>${t.kicks}</td><td>${F(t.bd, 1)}</td><td>${F(t.out15)}</td><td><b>${F(t.inOut)}</b></td><td>${chip(t.inOut - b.turns[i].inOut, 'lower')}</td><td>${chip(t.inOut - c.turns[i].inOut, 'lower')}</td></tr>`; }).join('')}
  </tbody></table></div>`;
  return `<div class="page">${head(key, 'Μικτή: ανά στυλ')}${body}${foot(n, N)}</div>`;
}

// ---- last page: checkpoints, phases, cumulative difference, glossary
function phaseTable(x) {
  const gs = groups(x);
  const best = gs.reduce((p, g) => p + Math.min(g.a, g.pb), 0);
  return `<div class="box"><table class="tbl"><thead><tr><th>Φάση</th><th>Αυτός ο αγώνας</th><th>ΑΡ</th><th>Αναφορά</th><th>Δ ΑΡ</th><th>Δ Αναφ.</th><th>Καλύτερη<br>του αθλητή</th></tr></thead><tbody>
    ${gs.map(g => `<tr><td class="l">${esc(g.label)}</td><td><b>${T(g.a)}</b></td><td>${T(g.pb)}</td><td>${T(g.ref)}</td><td>${chip(g.a - g.pb, 'lower')}</td><td>${chip(g.a - g.ref, 'lower')}</td><td>${T(Math.min(g.a, g.pb))} <span class="dim">${g.a <= g.pb ? 'αγώνας' : 'ΑΡ'}</span></td></tr>`).join('')}
    <tr class="tot"><td class="l">Σύνολο</td><td><b>${T(x.race.final)}</b></td><td>${T(x.pb.final)}</td><td>${T(x.ref.final)}</td><td>${chip(x.race.final - x.pb.final, 'lower')}</td><td>${chip(x.race.final - x.ref.final, 'lower')}</td><td><b>${T(best)}</b></td></tr>
  </tbody></table></div><div class="ins"><div class="ins-t">Ο χρόνος με τις καλύτερες φάσεις</div><ul><li>${best < x.race.final - 0.005
    ? `Με την καλύτερη κάθε φάσης από αυτόν τον αγώνα και το ΑΡ ο αθλητής θα έκανε <b>${T(best)}</b> (${S(best - x.race.final)} s από αυτόν τον αγώνα): ο χρόνος που έχει ήδη δείξει ότι μπορεί, φάση φάση.`
    : `Σε κάθε φάση αυτός ο αγώνας ήταν ο καλύτερος του αθλητή: το επόμενο βήμα είναι οι φάσεις με τη μεγαλύτερη διαφορά από την αναφορά.`}</li></ul></div>`;
}

function cumChart(x, h) {
  const a = x.race, b = x.pb, c = x.ref, D = a.p.distance;
  const pts = D <= 100 ? a.rows.map(r => r.d) : a.blocks.map(bk => bk.to);
  return section('Διαφορά χρόνου σε κάθε πέρασμα', 'Πόσο μπροστά (κάτω από το μηδέν) ή πίσω (πάνω από το μηδέν) ήταν ο αθλητής σε κάθε σημείο του αγώνα.') +
    `<div class="box">${ch.lineChart({ w: W - 24, h, cats: pts.map(String), fmt: v => S(v), tickFmt: v => (v > 0 ? '+' : v < 0 ? '−' : '') + Math.abs(v).toFixed(1),
      refLine: { value: 0, label: '' }, yTitle: 's', every: pts.length > 24 ? 2 : 1, xTitle: 'απόσταση (μ.)',
      series: [{ name: 'από το ΑΡ', color: C.race, values: pts.map(d => a.byD[d].t - b.byD[d].t) }, { name: 'από την αναφορά', color: C.ref, dash: 'dot', marker: 'diamond', values: pts.map(d => a.byD[d].t - c.byD[d].t) }] })}</div>`;
}

function pageTable(x, key, n, N, opts) {
  const a = x.race, b = x.pb, c = x.ref, D = a.p.distance;
  let body = '';
  if (opts.table) {
    body += section('Όλα τα περάσματα', 'Ο χρόνος σε κάθε πέρασμα της ανάλυσης. Με απαλό πορτοκαλί τα τμήματα εκκίνησης, στροφής και τερματισμού (η ταχύτητά τους περιέχει πτήση, υποβρύχια φάση ή γλίστρημα).');
    if (a.rows.length <= 20) body += checkpointTable(x, a.rows.length);
    else {
      const cols = a.rows.length > 60 ? 3 : 2, per = Math.ceil(a.rows.length / cols), withSr = cols === 2;
      let html = '<div class="cols">';
      for (let k = 0; k < cols; k++) {
        const part = a.rows.slice(k * per, (k + 1) * per);
        html += `<div class="box"><table class="tbl xs"><thead><tr><th>Πέρασμα</th><th>Χρόνος</th><th>Τμήμα</th><th>m/s</th>${withSr ? '<th>Χεριές</th><th>Συχν.</th>' : ''}<th>Δ ΑΡ</th><th>Δ Αναφ.</th></tr></thead><tbody>
          ${part.map(r => `<tr class="${r.isFree ? '' : 'phase'}"><td class="l"><b>${r.d}</b></td><td><b>${T(r.t)}</b></td><td>${F(r.seg)}</td><td>${F(r.vel)}</td>${withSr ? `<td>${r.isFree ? F(r.strokes, 1) : ''}</td><td>${r.sr ? F(r.sr, 1) : ''}</td>` : ''}<td>${chip(r.t - b.byD[r.d].t, 'lower')}</td><td>${chip(r.t - c.byD[r.d].t, 'lower')}</td></tr>`).join('')}
          </tbody></table></div>`;
      }
      body += html + '</div>';
    }
  }
  if (opts.phases) {
    body += section('Σύγκριση ανά φάση', 'Ο χρόνος κάθε φάσης του αγώνα δίπλα στο ΑΡ και στην αναφορά.');
    body += phaseTable(x);
  }
  if (opts.cum) body += cumChart(x, opts.cumH || 200);
  if (opts.glossary) body += glossary(x);
  return `<div class="page">${head(key, opts.title)}${body}${foot(n, N)}</div>`;
}

function glossary(x) {
  const list = [
    ['Καθαρή κολύμβηση', 'τα τμήματα χωρίς εκκίνηση, στροφές και τερματισμό: από 15 μ. μετά τον τοίχο ως 5 μ. πριν τον επόμενο.'],
    ['Συχνότητα', 'κύκλοι χεριών ανά λεπτό (κύκλος = δύο χεριές στο ελεύθερο και στο ύπτιο, μία στην πεταλούδα και στο πρόσθιο).'],
    ['Tempo', 'δευτερόλεπτα ανά κύκλο χεριών (60 ÷ συχνότητα): ο ρυθμός των χεριών όπως τον δίνει ένα tempo trainer.'],
    ['Μήκος κύκλου', 'μέτρα ανά κύκλο χεριών· η απόσταση ανά χεριά είναι το μισό του στο ελεύθερο και στο ύπτιο.'],
    ['SI', 'δείκτης κολύμβησης = ταχύτητα × μήκος κύκλου (m²/s)· μεγαλύτερος = πιο αποδοτική κολύμβηση στην ίδια ταχύτητα.'],
    ['ΔΑΚ', 'χεριές και δευτερόλεπτα από τα 15 μ. ως τον τοίχο, ανά 10 μ.· όσο μικρότερος, τόσο πιο αποδοτικό το κολύμπι.'],
    ['Swolf', 'χρόνος + χεριές του 50άρι.'],
    ['Στροφή −5/+15', 'ο χρόνος από 5 μ. πριν ως 15 μ. μετά τον τοίχο.'],
    ['Ρυθμός αγώνα', 'πώς μοιράζεται ο χρόνος στα 50άρια: σταθερός, θετικός (πιο αργό 2ο μισό) ή αρνητικός (πιο γρήγορο 2ο μισό).'],
    ['Διακύμανση', 'η τυπική απόκλιση των 50άρι ως ποσοστό του μέσου όρου τους (χωρίς το 1ο, που έχει την εκκίνηση).'],
    ['ΑΡ', 'ατομικό ρεκόρ: ο καλύτερος χρόνος του αθλητή στο αγώνισμα, από αγώνα με ανάλυση.'],
    ['Αναφορά', 'ο αγώνας σύγκρισης που διαλέγει ο αναλυτής (π.χ. ένας τελικός).']
  ];
  return section('Επεξήγηση όρων', '') + `<div class="gloss">${list.map(([k, v]) => `<div><b>${esc(k)}</b>: ${esc(v)}</div>`).join('')}</div>`;
}

// ---- documents
const LAST = { title: 'Περάσματα, σύγκριση, όροι', table: true, cum: true, glossary: true };
const PLAN = {
  '50': x => [p => pageOverview(x, '50', ...p), p => pageSpeed50(x, '50', ...p), p => pageStart(x, '50', ...p),
    p => pageTable(x, '50', ...p, Object.assign({}, LAST, { phases: true, cumH: 190 }))],
  '100': x => [p => pageOverview(x, '100', ...p), p => pagePace(x, '100', ...p), p => pageStrokes(x, '100', ...p), p => pageStart(x, '100', ...p),
    p => pageTable(x, '100', ...p, Object.assign({}, LAST, { phases: true, cum: false }))],
  '200': x => [p => pageOverview(x, '200', ...p), p => pagePace(x, '200', ...p), p => pageStrokes(x, '200', ...p), p => pageStart(x, '200', ...p),
    p => pageTurns(x, '200', ...p), p => pageTable(x, '200', ...p, Object.assign({}, LAST, { cumH: 190 }))],
  '400': x => [p => pageOverview(x, '400', ...p), p => pagePace(x, '400', ...p), p => pageStrokes(x, '400', ...p), p => pageStart(x, '400', ...p),
    p => pageTurns(x, '400', ...p), p => pageTable(x, '400', ...p, Object.assign({}, LAST, { cumH: 210 }))],
  '400im': x => [p => pageOverview(x, '400im', ...p), p => pageStyles(x, '400im', ...p), p => pagePace(x, '400im', ...p), p => pageStrokes(x, '400im', ...p),
    p => pageStart(x, '400im', ...p), p => pageTurns(x, '400im', ...p), p => pageTable(x, '400im', ...p, Object.assign({}, LAST, { cumH: 210 }))],
  '800': x => [p => pageOverview(x, '800', ...p), p => pagePace(x, '800', ...p), p => pageStrokes(x, '800', ...p), p => pagePer50(x, '800', ...p),
    p => pageStart(x, '800', ...p), p => pageTurns(x, '800', ...p), p => pageTable(x, '800', ...p, Object.assign({}, LAST, { cumH: 200 }))],
  '1500': x => [p => pageOverview(x, '1500', ...p), p => pagePace(x, '1500', ...p), p => pageStrokes(x, '1500', ...p), p => pagePer50(x, '1500', ...p),
    p => pageStart(x, '1500', ...p), p => pageTurns(x, '1500', ...p), p => pageTable(x, '1500', ...p, Object.assign({}, LAST, { cumH: 175 }))]
};

function document(key) {
  const x = R[key];
  const fns = PLAN[key](x);
  const N = fns.length;
  const pages = fns.map((f, i) => f([i + 1, N])).join('\n');
  return `<!doctype html><html lang="el"><head><meta charset="utf-8"><title>${esc(META[key].title)} · Πρότυπο αναφοράς</title><style>${CSS}</style></head><body>${pages}</body></html>`;
}

const CSS = `
@page { size: A4; margin: 0; }
* { box-sizing: border-box; }
html, body { margin: 0; padding: 0; background: #fff; }
body { font-family: 'Inter', 'DejaVu Sans', sans-serif; color: ${C.text}; font-size: 10px; line-height: 1.35; -webkit-print-color-adjust: exact; print-color-adjust: exact;
  font-variant-numeric: tabular-nums; font-feature-settings: 'tnum' 1, 'cv11' 1; }
.page { width: 794px; height: 1123px; position: relative; overflow: hidden; page-break-after: always; break-after: page; padding: 0 36px; background: #fff; }
.band { margin: 0 -36px; background: ${C.ink}; color: #fff; padding: 18px 36px 20px; position: relative; }
.band::after { content: ''; position: absolute; left: 0; right: 0; bottom: 0; height: 3px; background: #00C8FF; }
.band-top { display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px; }
.kicker { font-size: 9px; letter-spacing: 0.14em; color: #8CA0B6; font-weight: 600; }
.proto { font-size: 8.5px; letter-spacing: 0.1em; color: #0B111A; background: #00C8FF; padding: 3px 8px; border-radius: 3px; font-weight: 700; }
.band-main { display: flex; justify-content: space-between; align-items: flex-end; gap: 20px; }
.event { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 30px; font-weight: 700; letter-spacing: -0.01em; line-height: 1.05; }
.event .cat { font-size: 15px; font-weight: 500; color: #8CA0B6; margin-left: 6px; }
.name { font-size: 15px; font-weight: 600; margin-top: 8px; }
.meta { font-size: 9.5px; color: #8CA0B6; margin-top: 4px; }
.hero { text-align: right; min-width: 230px; }
.hero-lab { font-size: 9px; letter-spacing: 0.12em; color: #8CA0B6; text-transform: uppercase; font-weight: 600; }
.hero-time { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 50px; font-weight: 800; letter-spacing: -0.02em; line-height: 1.0; margin: 2px 0 6px; }
.hero-sub { display: flex; gap: 12px; justify-content: flex-end; font-size: 10px; color: #C9D6E3; }
.band .chip.g { background: #1C55B8; color: #fff; }
.band .chip.l { background: #E57B22; color: #0B111A; }
.ph { display: flex; justify-content: space-between; align-items: center; padding: 16px 0 9px; border-bottom: 2px solid ${C.ink}; margin-bottom: 4px; }
.ph-l { font-size: 10px; color: ${C.dim}; }
.ph-ev { font-weight: 700; color: ${C.ink}; }
.ph-sep { margin: 0 6px; color: #B8C3CF; }
.ph-r { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 13px; font-weight: 700; color: ${C.ink}; }
.pf { position: absolute; left: 36px; right: 36px; bottom: 14px; display: flex; justify-content: space-between; font-size: 8px; color: #8A97A6; border-top: 1px solid #E3E8EE; padding-top: 6px; }
.sec { margin: 14px 0 7px; }
.sec-t { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 13.5px; font-weight: 700; color: ${C.navy}; display: flex; align-items: center; gap: 8px; }
.sec-t::before { content: ''; width: 4px; height: 13px; background: #00C8FF; border-radius: 2px; }
.sec-s { font-size: 9px; color: ${C.dim}; margin-top: 2px; max-width: 700px; }
.box { background: #fff; border: 1px solid #E3E8EE; border-radius: 8px; padding: 10px 12px; margin-bottom: 8px; }
.box.tight { padding: 6px 12px; }
.tiles { display: grid; grid-template-columns: repeat(4, 1fr); gap: 8px; margin-bottom: 4px; }
.tiles.t4 { grid-template-columns: repeat(4, 1fr); }
.tile { background: #F5F7FA; border: 1px solid #E6EBF1; border-radius: 8px; padding: 8px 10px 7px; min-width: 0; }
.t-lab { font-size: 8.5px; letter-spacing: 0.06em; text-transform: uppercase; color: ${C.dim}; font-weight: 600; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.t-val { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 21px; font-weight: 700; color: ${C.ink}; margin: 3px 0 4px; white-space: nowrap; }
.t-u { font-family: 'Inter', sans-serif; font-size: 9.5px; font-weight: 500; color: ${C.dim}; margin-left: 2px; }
.t-word { font-size: 12.5px; font-weight: 700; white-space: normal; display: inline-block; line-height: 1.15; }
.t-d { display: flex; justify-content: space-between; font-size: 8.5px; color: ${C.dim}; gap: 4px; }
.t-x { font-size: 8.5px; color: ${C.dim}; margin-top: 3px; }
.chip { display: inline-block; font-size: 8.5px; font-weight: 700; padding: 1px 5px; border-radius: 9px; line-height: 1.3; white-space: nowrap; }
.chip.g { background: #E1EAFB; color: #1C55B8; }
.chip.l { background: #FCE6D3; color: #A24A07; }
.chip.n { background: #EDF0F4; color: #5B6B7B; }
.finds { display: grid; grid-template-columns: 1fr 1fr 1fr; gap: 8px; margin-top: 10px; }
.fc { border-radius: 8px; padding: 9px 11px; background: #F5F7FA; border-top: 3px solid ${C.navy}; }
.fc.good { border-top-color: #1C55B8; background: #F1F5FD; }
.fc.work { border-top-color: #E57B22; background: #FDF6EF; }
.fc.aim { border-top-color: #0E8A7E; background: #EFF7F6; }
.fc-t { font-family: 'Inter Display', 'Inter', sans-serif; font-weight: 700; font-size: 11.5px; color: ${C.ink}; margin-bottom: 4px; }
.fc ul { margin: 0; padding-left: 14px; }
.fc li { margin: 3px 0; font-size: 9.2px; line-height: 1.38; }
.tbl { width: 100%; border-collapse: collapse; font-size: 9.3px; }
.tbl th { background: #E8EEF5; color: #2B3F55; font-weight: 600; font-size: 8.4px; padding: 4px 5px; text-align: right; line-height: 1.2; }
.tbl th:first-child { text-align: left; border-top-left-radius: 5px; }
.tbl th:last-child { border-top-right-radius: 5px; }
.tbl td { padding: 3.2px 5px; text-align: right; border-bottom: 1px solid #EEF2F6; white-space: nowrap; }
.tbl td.l { text-align: left; }
.tbl tr.phase td { background: #FDF4EA; }
.tbl tr.tot td { background: #F1F5F9; font-weight: 600; border-top: 1px solid #D3DCE6; }
.tbl tr.tot td b { font-weight: 700; }
.tbl.sm { font-size: 8.8px; } .tbl.sm td { padding: 2.6px 4px; } .tbl.sm th { padding: 3px 4px; font-size: 8px; }
.tbl.xs { font-size: 8.2px; } .tbl.xs td { padding: 1.6px 4px; } .tbl.xs th { padding: 3px 4px; font-size: 7.8px; }
.tbl .chip { font-size: 7.8px; padding: 0 4px; }
.tbl th.g1 { background: #DDE6F6; color: #123B8A; text-align: center; } .tbl th.g2 { background: #E7EFFB; color: #2B5BA8; text-align: center; }
.tbl td.sm { font-size: 8px; }
.dim { color: #8A97A6; }
.lgrow { display: flex; gap: 16px; margin: 6px 0 0 96px; font-size: 9px; color: ${C.text}; }
.lgrow.c { margin: -2px 0 6px 0; justify-content: center; }
.lgi { display: inline-flex; align-items: center; gap: 5px; }
.lgi i { width: 11px; height: 9px; border-radius: 2px; display: inline-block; }
.lgi b.ln { width: 20px; height: 0; border-top: 2px solid; display: inline-block; }
.lgi b.ln.dash { border-top-style: dashed; } .lgi b.ln.dot { border-top-style: dotted; }
.two { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; }
.cols { display: grid; grid-template-columns: repeat(auto-fit, minmax(0, 1fr)); gap: 8px; }
.cols .box { padding: 6px 6px; }
.ins { background: #F1F5FD; border-left: 3px solid #1C55B8; border-radius: 6px; padding: 8px 12px; margin-top: 2px; }
.ins-t { font-family: 'Inter Display', 'Inter', sans-serif; font-weight: 700; font-size: 11px; color: ${C.ink}; margin-bottom: 3px; }
.ins ul { margin: 0; padding-left: 14px; } .ins li { margin: 2px 0; font-size: 9.2px; }
.gloss { display: grid; grid-template-columns: 1fr 1fr; gap: 4px 18px; font-size: 9px; color: ${C.text}; line-height: 1.4; }
svg.chart { display: block; overflow: visible; }
svg text { font-family: 'Inter', 'DejaVu Sans', sans-serif; }
.ax { font-size: 8.5px; fill: ${C.dim}; }
.lg { font-size: 9px; fill: ${C.text}; }
.pl { font-size: 8.2px; fill: ${C.text}; font-weight: 600; }
.el { font-size: 9px; }
.an { font-size: 8.5px; fill: ${C.dim}; }
.tl { font-size: 9.5px; fill: ${C.text}; }
.pv { font-size: 9px; font-weight: 700; }
.nt { font-size: 9px; fill: ${C.dim}; }
.sb { font-size: 8.8px; fill: #fff; font-weight: 700; }
.sv { font-size: 8.5px; fill: #fff; }
.tv { font-size: 11px; font-weight: 700; fill: ${C.ink}; }
.zn { font-size: 9.5px; fill: ${C.navy}; font-weight: 600; }
.et { font-size: 10px; font-weight: 700; fill: ${C.ink}; }
.es { font-size: 9px; fill: ${C.dim}; }
.sv2 { font-size: 9.5px; font-weight: 700; fill: ${C.text}; }
`;

module.exports = { document, META };
