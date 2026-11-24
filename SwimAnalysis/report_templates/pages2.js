// Report templates, 2nd version: every event (50 m, 25 m, 25 yd; all styles, medley, relays), bigger type, ΔΑΚ as the main
// index, recommendations from the numbers. Fictional data only (sim2.js).
'use strict';
const { event, STYLE_NAME, IM_ORDER } = require('./sim2');
const { recommendations, messages, legPbs, T, F, S, avg, sum, FLY_BENEFIT } = require('./recs');
const ch = require('./charts');
const { C, esc } = ch;
const W = 726;

function chip(x, better, n, suffix) {
  if (x === null || x === undefined || !isFinite(x)) return '';
  const k = Math.pow(10, n === undefined ? 2 : n);
  const zero = Math.round(Math.abs(x) * k) === 0;
  const good = better === 'lower' ? x < 0 : better === 'higher' ? x > 0 : null;
  const cls = zero || good === null ? 'chip n' : good ? 'chip g' : 'chip l';
  return `<span class="${cls}">${S(x, n)}${suffix || ''}</span>`;
}

// ---- names
function info(x) {
  const ev = x.ev, yd = ev.unit === 'yd', u = yd ? ' yd' : ' μ.', uu = yd ? ' yd' : 'μ.';
  const legLen = ev.distance / 4;
  const style = ev.style === 'im' ? (ev.relay ? 'Μικτή' : 'Μικτή Ατομική') : STYLE_NAME[ev.style];
  const title = ev.relay ? `4×${legLen}${uu} ${style}` : `${ev.distance}${uu} ${style}`;
  return { ev, yd, u, uu, title, relay: ev.relay, im: ev.style === 'im', D: ev.distance, P: ev.pool,
    who: ev.relay ? 'Ομάδα Δείγμα' : 'Αθλητής Δείγμα', kind: ev.relay ? 'Ομαδικό' : 'Ανδρών',
    pbName: ev.relay ? 'Ρεκόρ ομάδας' : 'Ατομικό ρεκόρ', pbShort: ev.relay ? 'Ρεκ. ομάδας' : 'ΑΡ', refName: 'Αναφορά', pool: ev.poolName,
    swimmers: ['Κολυμβητής Α', 'Κολυμβητής Β', 'Κολυμβητής Γ', 'Κολυμβητής Δ'] };
}

// ---- frame
function band(x, I) {
  const a = x.race, b = x.pb, c = x.ref;
  return `<div class="band">
    <div class="band-top"><span class="kicker">ΑΝΑΛΥΣΗ ΑΓΩΝΑ · RACE ANALYSIS</span><span class="proto">ΠΡΟΤΥΠΟ · ΦΑΝΤΑΣΤΙΚΑ ΔΕΔΟΜΕΝΑ</span></div>
    <div class="band-main">
      <div class="who">
        <div class="event">${esc(I.title)} <span class="cat">${esc(I.kind)}</span></div>
        <div class="name">${esc(I.who)}</div>
        <div class="meta">Αγώνας δείγμα · Δεκέμβριος 2026 · ${esc(I.pool)} · Τελικός Α · Διαδρομή 4</div>
      </div>
      <div class="hero">
        <div class="hero-lab">Τελικός χρόνος · 2η θέση</div>
        <div class="hero-time">${T(a.final)}</div>
        <div class="hero-sub"><span>${esc(I.pbShort)} ${T(b.final)} ${chip(a.final - b.final, 'lower')}</span><span>Αναφ. ${T(c.final)} ${chip(a.final - c.final, 'lower')}</span></div>
      </div>
    </div>
  </div>`;
}
function head(I, title) {
  return `<div class="ph"><div class="ph-l"><span class="ph-ev">${esc(I.title)}</span><span class="ph-sep">·</span>${esc(I.who)}<span class="ph-sep">·</span>${esc(I.pool)}</div><div class="ph-r">${esc(title)}</div></div>`;
}
const foot = (n, N) => `<div class="pf"><span>Kinovea Swim · Ανάλυση αγώνα · Πρότυπο αναφοράς με φανταστικά δεδομένα</span><span>Σελίδα ${n} από ${N}</span></div>`;
const section = (t, s) => `<div class="sec"><div class="sec-t">${esc(t)}</div>${s ? `<div class="sec-s">${s}</div>` : ''}</div>`;
const page = (I, title, body, n, N, first) => `<div class="page${first ? ' p1' : ''}">${first ? '' : head(I, title)}${body}${foot(n, N)}</div>`;

function tile(label, value, unit, dPb, dRef, better, n, extra, I) {
  const deltas = dPb === null && dRef === null ? '' : `<div class="t-d"><span>${esc(I ? I.pbShort : 'ΑΡ')} ${chip(dPb, better, n) || '–'}</span><span>Αναφ. ${chip(dRef, better, n) || '–'}</span></div>`;
  return `<div class="tile"><div class="t-lab">${esc(label)}</div><div class="t-val">${value}<span class="t-u">${esc(unit || '')}</span></div>${deltas}${extra ? `<div class="t-x">${extra}</div>` : ''}</div>`;
}
const legend3 = I => `<div class="lgrow c">${[['Αυτός ο αγώνας', C.race, ''], [I.pbName, C.pb, 'dash'], [I.refName, C.ref, 'dot']].map(([t, col, d]) => `<span class="lgi"><b class="ln ${d}" style="border-color:${col}"></b>${esc(t)}</span>`).join('')}</div>`;
const three = (x, I, f, from) => [{ name: 'Αυτός ο αγώνας', color: C.race, values: from(x.race).map(f) },
  { name: I.pbName, color: C.pb, dash: 'dash', marker: 'square', values: from(x.pb).map(f) },
  { name: I.refName, color: C.ref, dash: 'dot', marker: 'diamond', values: from(x.ref).map(f) }];
const realTurns = r => r.turns.filter(t => t.kind !== 'exchange');
const legLines = (x, cats) => x.ev.relay ? [1, 2, 3].map(k => ({ at: Math.round(cats.length * k / 4), label: 'Σκέλος ' + (k + 1) })) : [];

// ---- page 1: overview
function scorecard(x, I) {
  const a = x.race, b = x.pb, c = x.ref, tA = realTurns(a);
  const rows = [
    ['ΔΑΚ (μ.ο. 50άρι)', a.avg.dak, b.avg.dak, c.avg.dak, 'lower', v => F(v), '', true],
    ['Εκκίνηση 15' + I.u, a.start.t15, b.start.t15, c.start.t15, 'lower', v => F(v), ' s'],
    tA.length ? ['Στροφή −5/+15 (μ.ο.)', avg(tA, 'inOut'), avg(realTurns(b), 'inOut'), avg(realTurns(c), 'inOut'), 'lower', v => F(v), ' s'] : null,
    I.relay ? ['Αλλαγές (μ.ο.)', avg(a.legs.slice(1), 'ex'), avg(b.legs.slice(1), 'ex'), avg(c.legs.slice(1), 'ex'), 'lower', v => F(v), ' s'] : null,
    ['Ταχύτητα κολύμβησης', a.avg.vClean, b.avg.vClean, c.avg.vClean, 'higher', v => F(v), ' m/s'],
    ['Μήκος κύκλου', a.avg.sl, b.avg.sl, c.avg.sl, 'higher', v => F(v), ' μ.'],
    ['Συχνότητα · tempo', a.avg.sr, b.avg.sr, c.avg.sr, 'none', v => F(v, 1), ' κύκλ./λ.', false, `tempo ${F(a.avg.tempo)} s`],
    ['Τερματισμός 5' + I.u, a.finish.last5, b.finish.last5, c.finish.last5, 'lower', v => F(v), ' s']
  ].filter(r => r);
  return `<table class="sc"><thead><tr><th>Δείκτης</th><th>Αγώνας</th><th class="bh">← χειρότερα · καλύτερα →</th><th>${esc(I.pbShort)}</th><th>Αναφ.</th></tr></thead><tbody>
    ${rows.map(([lab, va, vb, vc, better, f, unit, main, sub]) => `<tr${main ? ' class="main"' : ''}><td class="l">${esc(lab)}${sub ? `<div class="sub">${esc(sub)}</div>` : ''}</td><td class="v"><b>${f(va)}</b><span class="u">${esc(unit)}</span></td>
      <td class="b">${ch.bullet({ w: 270, a: va, pb: vb, ref: vc, better: better === 'none' ? 'higher' : better })}</td><td>${chip(va - vb, better, f(1) === '1.0' ? 1 : 2)}</td><td>${chip(va - vc, better, f(1) === '1.0' ? 1 : 2)}</td></tr>`).join('')}
  </tbody></table>`;
}

function raceStrip(x, I) {
  const a = x.race;
  if (a.blocks.length <= 2 && !I.relay) {
    const items = a.rows.map(r => ({ label: `${r.prevD}–${r.d}`, time: F(r.seg), dak: r.isFree ? 0 : 0.3, dakText: r.isFree ? F(r.vel) + ' m/s' : F(r.vel) + ' m/s', mark: false }));
    return ch.heatStrip({ w: W - 26, items: items.map(it => Object.assign(it, { dakText: it.dakText })), neutral: true }).replace(/ΔΑΚ /g, '');
  }
  const items = a.blocks.map(bk => ({ label: `${bk.from}–${bk.to}`, time: T(bk.time), dak: bk.dak, dakText: F(bk.dak) }));
  return ch.heatStrip({ w: W - 26, items, neutral: x.ev.style === 'im' });
}

function pageOverview(x, I, R, n, N) {
  const a = x.race, b = x.pb, c = x.ref;
  const msgs = messages(x, R);
  const dak = a.avg.dak;
  const hero = `<div class="heros">
    <div class="hc dak"><div class="hc-lab">ΔΑΚ · ο βασικός δείκτης</div><div class="hc-val">${F(dak)}</div>
      <div class="hc-sub">${esc(I.pbShort)} ${F(b.avg.dak)} ${chip(dak - b.avg.dak, 'lower')} · Αναφ. ${F(c.avg.dak)} ${chip(dak - c.avg.dak, 'lower')}</div>
      <div class="hc-x">χεριές + δευτερόλεπτα ανά 10 μ. · όσο μικρότερος τόσο καλύτερα</div></div>
    <div class="hc"><div class="hc-lab">${x.race.final < x.pb.final ? (I.relay ? 'Νέο ρεκόρ ομάδας' : 'Νέο ατομικό ρεκόρ') : 'Από το ' + I.pbShort}</div><div class="hc-val">${S(a.final - b.final)}<span class="hc-u"> s</span></div>
      <div class="hc-sub">${T(b.final)} → ${T(a.final)}</div><div class="hc-x">από την αναφορά ${S(a.final - c.final)} s (${T(c.final)})</div></div>
    <div class="hc aim"><div class="hc-lab">Στόχος με τις προτάσεις</div><div class="hc-val">${T(R.targetTime)}</div>
      <div class="hc-sub">${S(R.targetTime - a.final)} s από αυτόν τον αγώνα</div><div class="hc-x">από ${Math.min(4, R.recs.filter(r => r.gain !== null).length)} προτάσεις της σελίδας 2</div></div>
  </div>`;
  const strip = section(a.blocks.length <= 2 && !I.relay ? 'Ο αγώνας ανά τμήμα' : 'Ο αγώνας ανά 50άρι', a.blocks.length <= 2 && !I.relay ? 'Ο χρόνος και η ταχύτητα κάθε τμήματος.'
    : (x.ev.style === 'im' ? 'Ο χρόνος και ο ΔΑΚ κάθε 50άρι (ο ΔΑΚ συγκρίνεται μόνο μέσα στο ίδιο στυλ).' : 'Ο χρόνος και ο ΔΑΚ κάθε 50άρι· μπλε: ΔΑΚ καλύτερος από τον μέσο όρο του αγώνα, πορτοκαλί: χειρότερος.')) + `<div class="box">${raceStrip(x, I)}</div>`;
  const key = `<span class="kk"><span class="k a"></span>αγώνας <span class="k p"></span>${esc(I.pbShort)} <span class="k r"></span>αναφορά</span>`;
  const body = band(x, I) + hero + section('Οι δείκτες του αγώνα', key + ' Κάθε δείκτης δίπλα στο ' + (I.relay ? 'ρεκόρ της ομάδας' : 'ατομικό ρεκόρ (ΑΡ)') + ' και την αναφορά· μπλε: καλύτερα, πορτοκαλί: χειρότερα.') +
    `<div class="box">${scorecard(x, I)}</div>` + strip + section('Τα 3 βασικά μηνύματα', '') +
    `<div class="msgs">${msgs.map((m, i) => `<div class="msg"><span class="num">${i + 1}</span><div>${m}</div></div>`).join('')}</div>`;
  return page(I, '', body, n, N, true);
}

// ---- page 2: recommendations
function pageRecs(x, I, R, n, N) {
  const a = x.race;
  const list = R.recs.slice(0, 5);
  const top = `<div class="aim-card"><div><div class="ac-lab">Στόχος για τον επόμενο αγώνα</div><div class="ac-val">${T(R.targetTime)}</div></div>
    <div class="ac-txt">Αν γίνουν οι ${Math.min(4, R.recs.filter(r => r.gain !== null).length)} πρώτες προτάσεις, με το μισό της διαφοράς από την αναφορά σε κάθε φάση: <b>${S(R.targetTime - a.final)} s</b> από τον σημερινό χρόνο (${T(a.final)}).</div></div>`;
  const cards = list.map((r, i) => `<div class="rc">
    <div class="rc-h"><span class="rc-n">${i + 1}</span><span class="rc-t">${esc(r.title)}</span>${r.gain !== null ? `<span class="rc-g">≈ ${S(-r.gain)} s</span>` : '<span class="rc-g n">ρυθμός</span>'}</div>
    <div class="rc-b"><div class="rc-col"><div class="rc-l">Τι δείχνουν τα νούμερα</div><ul>${r.facts.map(f => `<li>${f}</li>`).join('')}</ul></div>
      <div class="rc-col"><div class="rc-l">Πρόταση</div><div class="rc-a">${r.action}</div><div class="rc-l">Στόχος</div><div class="rc-a">${r.target}</div></div></div>
  </div>`).join('');
  const body = section('Προτάσεις με βάση τα νούμερα', 'Με σειρά κέρδους: πόσος χρόνος κερδίζεται αν η φάση φτάσει στη μέση της απόστασης από την αναφορά.') + top + cards;
  return page(I, 'Προτάσεις', body, n, N);
}

// ---- page 3: ΔΑΚ
function pageDak(x, I, n, N) {
  const a = x.race, b = x.pb, c = x.ref, bl = a.blocks;
  let body = '';
  const ex = bl[0];
  const div = ex.winTime / ex.dakTime;
  const how = `<div class="ins dakx"><div class="ins-t">Πώς βγαίνει ο ΔΑΚ</div><ul>
    <li>ΔΑΚ = (χεριές + δευτερόλεπτα από τα 15${I.u} ως τον τοίχο) ÷ μέτρα του παραθύρου ÷ 10${I.P === 25 ? ', σε κάθε μήκος του 50άρι' : ''}.</li>
    <li>Στο 1ο 50άρι: (<b>${F(ex.winStrokes, 1)}</b> χεριές + <b>${F(ex.winTime)}</b> s) ÷ ${F(div, div % 1 ? 2 : 1)} = <b>${F(ex.dak)}</b>.</li>
    <li>Όσο μικρότερος, τόσο λιγότερες χεριές και λιγότερος χρόνος για την ίδια απόσταση: πιο αποδοτικό κολύμπι.</li></ul></div>`;
  if (bl.length === 1) {
    body += section('ΔΑΚ', 'Ο βασικός δείκτης του αγώνα, δίπλα στο ' + I.pbShort + ' και την αναφορά.');
    body += `<div class="tiles">${tile('ΔΑΚ', F(a.avg.dak), '', a.avg.dak - b.avg.dak, a.avg.dak - c.avg.dak, 'lower', 2, null, I)}
      ${tile('Χεριές στο παράθυρο', F(ex.winStrokes, 1), '', ex.winStrokes - b.blocks[0].winStrokes, ex.winStrokes - c.blocks[0].winStrokes, 'lower', 1, null, I)}
      ${tile('Χρόνος στο παράθυρο', F(ex.winTime), ' s', ex.winTime - b.blocks[0].winTime, ex.winTime - c.blocks[0].winTime, 'lower', 2, null, I)}
      ${tile('Ταχύτητα κολύμβησης', F(a.avg.vClean), ' m/s', a.avg.vClean - b.avg.vClean, a.avg.vClean - c.avg.vClean, 'higher', 2, null, I)}</div>`;
    body += section('Από τι αποτελείται', 'Το μέρος των χεριών και το μέρος του χρόνου, για τον αγώνα, το ' + I.pbShort + ' και την αναφορά.');
    body += `<div class="box">${ch.vStacked({ w: W - 26, h: 260, cats: ['Αυτός ο αγώνας', I.pbName, I.refName], totalFmt: v => F(v),
      parts: [{ name: 'Χεριές ανά 10 μ.', color: C.p1, values: [a, b, c].map(r => r.blocks[0].dakStrokes) }, { name: 'Δευτερόλεπτα ανά 10 μ.', color: '#F2A65A', values: [a, b, c].map(r => r.blocks[0].dakTime) }] })}</div>`;
    body += how;
  } else {
    const h = Math.floor(bl.length / 2);
    const best = bl.reduce((p, q) => q.dak < p.dak ? q : p), worst = bl.reduce((p, q) => q.dak > p.dak ? q : p);
    const rise = avg(bl.slice(h), 'dak') - avg(bl.slice(0, h), 'dak');
    const mixedStyles = x.ev.style === 'im';
    const byStyle = mixedStyles ? ['fly', 'bk', 'br', 'fs'].map(st => ({ st, a: bl.filter(z => z.style === st), b: b.blocks.filter(z => z.style === st), c: c.blocks.filter(z => z.style === st) }))
      .filter(g => g.a.length).sort((p, q) => bl.indexOf(p.a[0]) - bl.indexOf(q.a[0])) : null;
    body += section('ΔΑΚ: ο βασικός δείκτης', 'Ο ΔΑΚ κάθε 50άρι δίπλα στο ' + I.pbShort + ' και την αναφορά' + (mixedStyles ? '· σε κάθε στυλ συγκρίνεται με το ίδιο στυλ.' : '.'));
    if (mixedStyles) body += `<div class="tiles">${byStyle.map(g => tile('ΔΑΚ · ' + STYLE_NAME[g.st], F(avg(g.a, 'dak')), '', avg(g.a, 'dak') - avg(g.b, 'dak'), avg(g.a, 'dak') - avg(g.c, 'dak'), 'lower', 2, null, I)).join('')}</div>`;
    else body += `<div class="tiles">${tile('ΔΑΚ μ.ο.', F(a.avg.dak), '', a.avg.dak - b.avg.dak, a.avg.dak - c.avg.dak, 'lower', 2, null, I)}
      ${tile('Καλύτερο 50άρι', F(best.dak), '', null, null, 'none', 2, `${best.from}–${best.to}${I.u}`, I)}
      ${tile('Χειρότερο 50άρι', F(worst.dak), '', null, null, 'none', 2, `${worst.from}–${worst.to}${I.u}`, I)}
      ${tile('2ο μισό − 1ο', S(rise), '', null, null, 'none', 2, rise >= 0.3 ? 'ανεβαίνει στο 2ο μισό' : 'σταθερός', I)}</div>`;
    const cats = bl.map(bk => String(bk.to));
    const mid = bl.length > 4 && bl.length <= 8;
    body += `<div class="box">${ch.lineChart({ w: W - 26, h: bl.length > 16 ? 215 : mid ? 196 : 230, cats, fmt: v => v.toFixed(2), yTitle: 'ΔΑΚ', xTitle: '50άρι (' + I.uu.trim() + ')', every: bl.length > 16 ? 2 : 1,
      series: three(x, I, bk => bk.dak, r => r.blocks), vlines: legLines(x, cats) })}</div>`;
    body += section('Από τι αποτελείται ο ΔΑΚ', 'Σε κάθε 50άρι: χεριές ανά 10 μ. και δευτερόλεπτα ανά 10 μ.· έτσι φαίνεται αν ανεβαίνει από τις χεριές ή από τον χρόνο.');
    body += `<div class="box">${ch.vStacked({ w: W - 26, h: bl.length > 16 ? 200 : mid ? 178 : 215, cats, every: bl.length > 16 ? 2 : 1, totalFmt: v => F(v),
      parts: [{ name: 'Χεριές ανά 10 μ.', color: C.p1, values: bl.map(bk => bk.dakStrokes) }, { name: 'Δευτερόλεπτα ανά 10 μ.', color: '#F2A65A', values: bl.map(bk => bk.dakTime) }] })}</div>`;
    const first = bl.slice(0, h), second = bl.slice(h);
    const rS = avg(second, 'dakStrokes') - avg(first, 'dakStrokes'), rT = avg(second, 'dakTime') - avg(first, 'dakTime');
    const worstStyle = mixedStyles ? byStyle.slice().sort((p, q) => (avg(q.a, 'dak') - avg(q.c, 'dak')) - (avg(p.a, 'dak') - avg(p.c, 'dak')))[0] : null;
    body += `<div class="ins"><div class="ins-t">Τι λέει ο ΔΑΚ</div><ul>
      ${mixedStyles ? `<li>Η μεγαλύτερη διαφορά από την αναφορά στο <b>${esc(STYLE_NAME[worstStyle.st].toLowerCase())}</b>: ${F(avg(worstStyle.a, 'dak'))} έναντι ${F(avg(worstStyle.c, 'dak'))} (${S(avg(worstStyle.a, 'dak') - avg(worstStyle.c, 'dak'))}).</li>
      <li>${byStyle.map(g => `${esc(STYLE_NAME[g.st])} ${F(avg(g.a, 'dak'))}`).join(' · ')}: κάθε στυλ έχει τη δική του κλίμακα ΔΑΚ (π.χ. το πρόσθιο έχει λιγότερες χεριές).</li>`
      : `<li>1ο μισό <b>${F(avg(first, 'dak'))}</b> → 2ο μισό <b>${F(avg(second, 'dak'))}</b> (${S(rise)}): από τις χεριές ${S(rS)}, από τον χρόνο ${S(rT)} ανά 10 μ.</li>
      <li>Το καλύτερο 50άρι στα ${best.from}–${best.to}${I.u} (${F(best.dak)}), το χειρότερο στα ${worst.from}–${worst.to}${I.u} (${F(worst.dak)}).</li>`}
      <li>ΔΑΚ = (χεριές + δευτερόλεπτα από τα 15${I.u} ως τον τοίχο) ÷ μέτρα/10· στο 1ο 50άρι (${F(ex.winStrokes, 1)} + ${F(ex.winTime)}) ÷ ${F(div, div % 1 ? 2 : 1)} = ${F(ex.dak)}.</li></ul></div>`;
    if (bl.length <= 8) body += `<div class="box">${dakTable(x, I)}</div>`;
  }
  return page(I, 'ΔΑΚ', body, n, N);
}

function dakTable(x, I) {
  const a = x.race, b = x.pb, c = x.ref, bl = a.blocks;
  const step = bl.length > 16 ? 2 : 1;
  const rows = [];
  for (let i = 0; i < bl.length; i += step) {
    const g = bl.slice(i, i + step), gb = b.blocks.slice(i, i + step), gc = c.blocks.slice(i, i + step);
    rows.push({ from: g[0].from, to: g[g.length - 1].to, dak: avg(g, 'dak'), dPb: avg(g, 'dak') - avg(gb, 'dak'), dRef: avg(g, 'dak') - avg(gc, 'dak'),
      strokes: sum(g, 'winStrokes'), time: sum(g, 'winTime'), v: avg(g, 'vClean'), sr: avg(g, 'sr'), sl: avg(g, 'sl'), style: g[0].style });
  }
  return `<table class="tbl${rows.length > 10 ? ' sm' : ''}"><thead><tr><th>${step > 1 ? '100άρι' : '50άρι'}</th>${I.im || (I.relay && x.ev.style === 'im') ? '<th>Στυλ</th>' : ''}<th>ΔΑΚ</th><th>Δ ${esc(I.pbShort)}</th><th>Δ Αναφ.</th><th>Χεριές<br>παραθύρου</th><th>Χρόνος<br>παραθύρου</th><th>Ταχύτητα<br>m/s</th><th>Συχνότητα</th><th>Μήκος<br>κύκλου</th></tr></thead><tbody>
    ${rows.map(r => `<tr><td class="l">${r.from}–${r.to}</td>${I.im || (I.relay && x.ev.style === 'im') ? `<td class="l">${esc(STYLE_NAME[r.style])}</td>` : ''}<td class="dk"><b>${F(r.dak)}</b></td><td>${chip(r.dPb, 'lower')}</td><td>${chip(r.dRef, 'lower')}</td>
      <td>${F(r.strokes, 1)}</td><td>${F(r.time)}</td><td>${F(r.v)}</td><td>${F(r.sr, 1)}</td><td>${F(r.sl)}</td></tr>`).join('')}
    <tr class="tot"><td class="l">Μ.ο.</td>${I.im || (I.relay && x.ev.style === 'im') ? '<td></td>' : ''}<td class="dk"><b>${F(a.avg.dak)}</b></td><td>${chip(a.avg.dak - b.avg.dak, 'lower')}</td><td>${chip(a.avg.dak - c.avg.dak, 'lower')}</td><td></td><td></td><td>${F(a.avg.vClean)}</td><td>${F(a.avg.sr, 1)}</td><td>${F(a.avg.sl)}</td></tr>
  </tbody></table>`;
}

// ---- composition and phases
function groups(x) {
  const a = x.race, ev = x.ev, D = ev.distance, P = ev.pool;
  const unit = ev.relay ? D / 4 : D <= 200 ? 50 * (P === 25 && D >= 200 ? 2 : 1) : D <= 500 ? 100 : D <= 1000 ? 200 : 500;
  const out = [];
  const add = (label, pick) => {
    const t = r => r.phases.filter(pick).reduce((s, p) => s + p.t, 0);
    out.push({ label, a: t(x.race), pb: t(x.pb), ref: t(x.ref) });
  };
  const u = ev.unit === 'yd' ? ' yd' : ' μ.';
  add('Εκκίνηση 0–15' + u, p => p.kind === 'start');
  for (let s = 0; s < D; s += unit) {
    const e = s + unit;
    const lab = ev.relay ? `Σκέλος ${s / unit + 1}` : `${s}–${e}${u}`;
    const swims = a.phases.filter(p => p.kind === 'swim' && p.from >= s && p.to <= e + 1e-9);
    const swimLab = ev.relay ? `Κολύμβηση · ${lab}` : swims.length === 1 ? `Κολύμβηση ${swims[0].from}–${swims[0].to}${u}` : `Κολύμβηση στα ${s}–${e}${u}`;
    add(swimLab, p => p.kind === 'swim' && p.from >= s && p.to <= e + 1e-9);
    const walls = a.turns.filter(t => t.kind !== 'exchange' && t.wall > s && t.wall < e).length;
    if (walls) add(`${walls > 1 ? 'Στροφές' : 'Στροφή'} · ${lab}`, p => p.kind === 'turn' && p.wall > s && p.wall < e);
    if (ev.relay && e < D) add(`Αλλαγή ${s / unit + 1} → ${s / unit + 2}`, p => p.kind === 'exchange' && p.wall === e);
    if (!ev.relay && unit <= 50 && e < D && P === 50) add(`Στροφή ${e}${u}`, p => p.kind === 'turn' && p.wall === e);
  }
  add('Τερματισμός 5' + u, p => p.kind === 'finish');
  return out.filter(g => g.a > 0);
}

function compositionChart(x, I) {
  const parts = cmp => [
    { name: 'Εκκίνηση', value: cmp.start, color: C.p1 }, { name: 'Κολύμβηση', value: cmp.swim, color: C.p2 },
    { name: 'Στροφές', value: cmp.turn, color: C.p3 }, { name: 'Αλλαγές', value: cmp.exchange, color: '#8E5BB0' }, { name: 'Τερματισμός', value: cmp.finish, color: C.p4 }
  ].filter(p => p.value > 0);
  const lg = parts(x.race.composition).map(p => `<span class="lgi"><i style="background:${p.color}"></i>${esc(p.name)}</span>`).join('');
  return ch.stacked({ w: W - 26, fmtT: v => T(v) + ' s', rows: [
    { label: 'Αυτός ο αγώνας', parts: parts(x.race.composition), total: x.race.final },
    { label: I.pbName, parts: parts(x.pb.composition), total: x.pb.final },
    { label: I.refName, parts: parts(x.ref.composition), total: x.ref.final }] }) + `<div class="lgrow">${lg}</div>`;
}

function phaseTable(x, I) {
  const gs = groups(x);
  const best = gs.reduce((p, g) => p + Math.min(g.a, g.pb), 0);
  return `<table class="tbl${gs.length > 10 ? ' sm' : ''}"><thead><tr><th>Φάση</th><th>Αγώνας</th><th>${esc(I.pbShort)}</th><th>Αναφορά</th><th>Δ ${esc(I.pbShort)}</th><th>Δ Αναφ.</th><th>Καλύτερη</th></tr></thead><tbody>
    ${gs.map(g => `<tr><td class="l">${esc(g.label)}</td><td><b>${T(g.a)}</b></td><td>${T(g.pb)}</td><td>${T(g.ref)}</td><td>${chip(g.a - g.pb, 'lower')}</td><td>${chip(g.a - g.ref, 'lower')}</td><td>${T(Math.min(g.a, g.pb))}</td></tr>`).join('')}
    <tr class="tot"><td class="l">Σύνολο</td><td><b>${T(x.race.final)}</b></td><td>${T(x.pb.final)}</td><td>${T(x.ref.final)}</td><td>${chip(x.race.final - x.pb.final, 'lower')}</td><td>${chip(x.race.final - x.ref.final, 'lower')}</td><td><b>${T(best)}</b></td></tr>
  </tbody></table>`;
}

function pageComposition(x, I, n, N) {
  const gs = groups(x);
  const best = gs.reduce((p, g) => p + Math.min(g.a, g.pb), 0);
  let body = section('Σύνθεση του χρόνου', 'Πού πήγε ο χρόνος: εκκίνηση (0–15), καθαρή κολύμβηση, στροφές (−5/+15)' + (I.relay ? ', αλλαγές' : '') + ' και τερματισμός (τελευταία 5).');
  body += `<div class="box">${compositionChart(x, I)}</div>`;
  body += section('Πού κερδήθηκε και πού χάθηκε χρόνος', 'Διαφορά κάθε φάσης από το ' + I.pbShort + ' (μπάρες) και από την αναφορά (δεξιά).');
  body += `<div class="box tight">${ch.hDiverging({ w: W - 26, items: gs.map(g => ({ label: g.label, value: g.a - g.pb, note: `Αναφ. ${S(g.a - g.ref)}` })), labelW: 190, noteW: 100, rowH: gs.length > 10 ? 20.5 : 25 })}</div>`;
  body += section('Σύγκριση ανά φάση', '');
  body += `<div class="box">${phaseTable(x, I)}</div>`;
  body += `<div class="ins"><div class="ins-t">Ο χρόνος με τις καλύτερες φάσεις</div><ul><li>${best < x.race.final - 0.005
    ? `Με την καλύτερη κάθε φάσης από αυτόν τον αγώνα και το ${I.pbShort}: <b>${T(best)}</b> (${S(best - x.race.final)} s), ο χρόνος που ${I.relay ? 'η ομάδα έχει' : 'ο αθλητής έχει'} ήδη δείξει ότι μπορεί.`
    : 'Σε κάθε φάση αυτός ο αγώνας ήταν ο καλύτερος· το επόμενο βήμα είναι οι φάσεις με τη μεγαλύτερη διαφορά από την αναφορά.'}</li></ul></div>`;
  return page(I, 'Σύνθεση και φάσεις', body, n, N);
}

// ---- speed (50 / 100)
function pageSpeed(x, I, n, N) {
  const a = x.race, b = x.pb, c = x.ref, D = I.D;
  const cats = a.rows.map(r => `${r.prevD}–${r.d}`);
  let body = section('Καμπύλη ταχύτητας', 'Μέση ταχύτητα κάθε τμήματος· τα τμήματα εκκίνησης, στροφής και τερματισμού περιέχουν πτήση, υποβρύχια φάση ή γλίστρημα.');
  body += `<div class="box">${ch.lineChart({ w: W - 26, h: 240, cats, fmt: v => v.toFixed(2), yTitle: 'm/s', xTitle: 'τμήμα (' + I.uu.trim() + ')', every: cats.length > 12 ? 2 : 1,
    series: three(x, I, r => r.vel, r => r.rows) })}</div>`;
  const free = a.rows.filter(r => r.isFree);
  const vmax = free.reduce((p, q) => q.vel > p.vel ? q : p), vlast = free[free.length - 1];
  if (D === 100) {
    const h1 = a.blocks[0].time, h2 = a.blocks[1].time;
    body += `<div class="tiles">${tile('1ο 50άρι', T(h1), ' s', h1 - b.blocks[0].time, h1 - c.blocks[0].time, 'lower', 2, null, I)}
      ${tile('2ο 50άρι', T(h2), ' s', h2 - b.blocks[1].time, h2 - c.blocks[1].time, 'lower', 2, null, I)}
      ${tile('Διαφορά 2ου − 1ου', S(h2 - h1), ' s', (h2 - h1) - (b.blocks[1].time - b.blocks[0].time), (h2 - h1) - (c.blocks[1].time - c.blocks[0].time), 'lower', 2, null, I)}
      ${tile('Πτώση ταχύτητας', ((vmax.vel - vlast.vel) / vmax.vel * 100).toFixed(1), '%', null, null, 'none', 1, `${vmax.prevD}–${vmax.d} → ${vlast.prevD}–${vlast.d}${I.u}`, I)}</div>`;
  } else {
    body += `<div class="tiles">${tile('Μέγιστη ταχύτητα', F(vmax.vel), ' m/s', vmax.vel - b.byD[vmax.d].vel, vmax.vel - c.byD[vmax.d].vel, 'higher', 2, `${vmax.prevD}–${vmax.d}${I.u}`, I)}
      ${tile('Τελευταίο τμήμα', F(vlast.vel), ' m/s', vlast.vel - b.byD[vlast.d].vel, vlast.vel - c.byD[vlast.d].vel, 'higher', 2, `${vlast.prevD}–${vlast.d}${I.u}`, I)}
      ${tile('Πτώση ταχύτητας', ((vmax.vel - vlast.vel) / vmax.vel * 100).toFixed(1), '%', null, null, 'none', 1, 'από το πιο γρήγορο τμήμα στο τελευταίο', I)}
      ${tile('Τελευταία 5' + I.u, F(a.finish.vLast5), ' m/s', a.finish.vLast5 - b.finish.vLast5, a.finish.vLast5 - c.finish.vLast5, 'higher', 2, null, I)}</div>`;
  }
  body += section('Σύνθεση του χρόνου', 'Εκκίνηση (0–15), καθαρή κολύμβηση, στροφές (−5/+15) και τερματισμός (τελευταία 5).');
  body += `<div class="box">${compositionChart(x, I)}</div>`;
  const gs = groups(x);
  body += section('Πού κερδήθηκε και πού χάθηκε χρόνος', 'Διαφορά κάθε φάσης από το ' + I.pbShort + ' (μπάρες) και από την αναφορά (δεξιά).');
  body += `<div class="box tight">${ch.hDiverging({ w: W - 26, items: gs.map(g => ({ label: g.label, value: g.a - g.pb, note: `Αναφ. ${S(g.a - g.ref)}` })), labelW: 190, noteW: 100, rowH: 25 })}</div>`;
  return page(I, D === 100 ? 'Ρυθμός, ταχύτητα, σύνθεση' : 'Ταχύτητα και σύνθεση', body, n, N);
}

// ---- pace (200 and longer, relays)
function pagePace(x, I, R, n, N) {
  const a = x.race, b = x.pb, c = x.ref, D = I.D, bl = a.blocks;
  const cats = bl.map(bk => String(bk.to));
  const mixed = I.im;
  let body = section('Ρυθμός ανά 50άρι', mixed ? 'Ο χρόνος κάθε 50άρι, δίπλα στο ' + I.pbShort + ' και την αναφορά.' : `Ο χρόνος κάθε 50άρι· η διακεκομμένη γραμμή είναι ο μέσος όρος από το 2ο 50άρι.`);
  const mean = avg(bl.slice(1), 'time');
  body += `<div class="box">${ch.lineChart({ w: W - 26, h: 240, cats, fmt: v => T(v), yTitle: 's / 50', xTitle: '50άρι (' + I.uu.trim() + ')', every: bl.length > 16 ? 2 : 1,
    refLine: mixed || I.relay ? null : { value: mean, label: 'μ.ο.' }, series: three(x, I, bk => bk.time, r => r.blocks), vlines: legLines(x, cats) })}</div>`;
  const dev = mixed || I.relay ? bl.map((bk, i) => bk.time - b.blocks[i].time) : bl.map((bk, i) => i === 0 ? null : bk.time - mean);
  body += section(mixed || I.relay ? 'Διαφορά από το ' + I.pbShort + ' ανά 50άρι' : 'Απόκλιση από τον μέσο όρο', 'Κάτω από το μηδέν πιο γρήγορα (μπλε), πάνω πιο αργά (πορτοκαλί).');
  body += `<div class="box">${ch.vDiverging({ w: W - 26, h: 150, cats, values: dev, labels: bl.length <= 10, every: bl.length > 16 ? 2 : 1, yTitle: 's' })}</div>`;
  if (!mixed && !I.relay) {
    const nb = bl.length, h1 = sum(bl.slice(0, nb / 2), 'time'), h2 = sum(bl.slice(nb / 2), 'time');
    const sd = Math.sqrt(avg(bl.slice(1), bk => (bk.time - mean) * (bk.time - mean)));
    const fast = bl.slice(1).reduce((p, q) => q.time < p.time ? q : p), slow = bl.slice(1).reduce((p, q) => q.time > p.time ? q : p);
    const rel = (h2 - h1) / (h1 + h2);
    const type = rel < -0.002 ? 'Αρνητικός' : rel < 0.012 ? (bl[nb - 1].time < mean - 0.3 ? 'Σταθερός, δυνατό τέλος' : 'Σταθερός') : 'Θετικός';
    body += `<div class="tiles">${tile('Ρυθμός αγώνα', `<span class="t-word">${type}</span>`, '', null, null, 'none', 2, `2ο μισό ${S(h2 - h1)} s`, I)}
      ${tile('Διακύμανση 50άρι', (100 * sd / mean).toFixed(1), '%', null, null, 'none', 1, `τυπική απόκλιση ${F(sd)} s`, I)}
      ${tile('Πιο γρήγορο / αργό', `${T(fast.time)}<span class="t-u"> / </span>${T(slow.time)}`, '', null, null, 'none', 2, `στα ${fast.to} και ${slow.to}${I.u}`, I)}
      ${tile('Τελευταίο 100άρι', T(sum(bl.slice(-2), 'time')), ' s', sum(bl.slice(-2), 'time') - sum(b.blocks.slice(-2), 'time'), sum(bl.slice(-2), 'time') - sum(c.blocks.slice(-2), 'time'), 'lower', 2, null, I)}</div>`;
  }
  if (R.splits && !I.relay) {
    const k = bl.length > 16 ? 2 : 1;
    const rows = [];
    for (let i = 0; i < bl.length; i += k) rows.push({ from: bl[i].from, to: bl[Math.min(bl.length, i + k) - 1].to, a: sum(bl.slice(i, i + k), 'time'), t: R.splits.slice(i, i + k).reduce((p, q) => p + q, 0) });
    const per = Math.ceil(rows.length / 2);
    const table = list => `<table class="tbl${rows.length > 16 ? ' sm' : ''}"><thead><tr><th>${k > 1 ? '100άρι' : '50άρι'}</th><th>Αγώνας</th><th>Στόχος</th><th>Διαφορά</th></tr></thead><tbody>
      ${list.map(r => `<tr><td class="l">${r.from}–${r.to}</td><td>${T(r.a)}</td><td class="aimv"><b>${T(r.t)}</b></td><td>${chip(r.t - r.a, 'lower')}</td></tr>`).join('')}</tbody></table>`;
    body += section(`Χρόνοι-στόχοι για ${T(R.targetTime)}`, 'Ο στόχος της σελίδας των προτάσεων, μοιρασμένος με τον ρυθμό της αναφοράς.');
    body += `<div class="two"><div class="box">${table(rows.slice(0, per))}</div><div class="box">${table(rows.slice(per))}</div></div>`;
  } else {
    const per100 = [];
    const step = I.relay || I.im ? I.D / 4 : 100;
    for (let s = 0; s < D; s += step) {
      const e = s + step, t = r => r.byD[e].t - (s === 0 ? 0 : r.byD[s].t);
      per100.push({ s, e, a: t(a), pb: t(b), ref: t(c), lab: I.relay ? `Σκέλος ${s / step + 1}` : I.im ? STYLE_NAME[IM_ORDER[s / step]] : '' });
    }
    body += section(I.relay ? 'Χρόνοι ανά σκέλος' : I.im ? 'Χρόνοι ανά στυλ' : 'Χρόνοι ανά 100', '');
    body += `<div class="box"><table class="tbl"><thead><tr><th>${I.relay ? 'Σκέλος' : I.im ? 'Στυλ' : '100άρι'}</th><th>Χρόνος</th><th>Σωρευτικός</th><th>${esc(I.pbShort)}</th><th>Δ ${esc(I.pbShort)}</th><th>Αναφ.</th><th>Δ Αναφ.</th></tr></thead><tbody>
      ${per100.map(p => `<tr><td class="l">${p.s}–${p.e}${p.lab ? ' · ' + esc(p.lab) : ''}</td><td><b>${T(p.a)}</b></td><td>${T(a.byD[p.e].t)}</td><td>${T(p.pb)}</td><td>${chip(p.a - p.pb, 'lower')}</td><td>${T(p.ref)}</td><td>${chip(p.a - p.ref, 'lower')}</td></tr>`).join('')}
      </tbody></table></div>`;
  }
  return page(I, 'Ρυθμός αγώνα', body, n, N);
}

// ---- strokes
function pageStrokes(x, I, n, N) {
  const a = x.race, b = x.pb, c = x.ref, P = I.P, lengths = I.D / P;
  const pattern = x.race.sim.pre.pattern;
  let body = '';
  const charts = (cats, get, every, vl, h) => ['vel', 'sr', 'sl'].map((f, i) => `<div class="box">${ch.lineChart({ w: W - 26, h: h || 175, cats, every,
    fmt: v => f === 'sr' ? v.toFixed(1) : v.toFixed(2), yTitle: ['Ταχύτητα (m/s)', 'Συχνότητα (κύκλοι/λεπτό)', 'Μήκος κύκλου (μ.)'][i], series: get(f), vlines: vl })}</div>`).join('');
  if (P === 50 && pattern.length === 4) {
    // 15 · 25 · 45 · 50: the 1st and the 2nd 25 of every 50.
    const cats = a.blocks.map(bk => String(bk.to));
    const pair = f => [{ name: '1ο 25άρι (15–25)', color: C.first25, values: a.blocks.map(bk => bk.first25 ? bk.first25[f] : null) },
      { name: '2ο 25άρι (25–45)', color: C.second25, marker: 'square', values: a.blocks.map(bk => bk.second25 ? bk.second25[f] : null) }];
    body += section('Χεριές ανά 25άρι', 'Σε κάθε 50άρι: 1ο 25άρι = 15→25, 2ο 25άρι = 25→45 (καθαρή κολύμβηση). Ταχύτητα = συχνότητα × μήκος κύκλου.');
    body += charts(cats, pair, 1, legLines(x, cats), 165);
    body += section('Ανά 50άρι: 1ο και 2ο 25άρι', '');
    body += `<div class="box"><table class="tbl sm"><thead><tr><th rowspan="2">50άρι</th><th rowspan="2">Χρόνος</th><th colspan="4" class="g1">1ο 25άρι · 15→25</th><th colspan="4" class="g2">2ο 25άρι · 25→45</th><th rowspan="2">ΔΑΚ</th></tr>
      <tr><th class="g1">m/s</th><th class="g1">κύκλ./λ.</th><th class="g1">tempo</th><th class="g1">μήκος</th><th class="g2">m/s</th><th class="g2">κύκλ./λ.</th><th class="g2">tempo</th><th class="g2">μήκος</th></tr></thead><tbody>
      ${a.blocks.map(bk => { const f = bk.first25, s = bk.second25; return `<tr><td class="l">${bk.from}–${bk.to}${I.im || I.relay ? ' · ' + esc(STYLE_NAME[bk.style]) : ''}</td><td><b>${T(bk.time)}</b></td>
        <td>${F(f.vel)}</td><td>${F(f.sr, 1)}</td><td>${F(f.tempo)}</td><td>${F(f.sl)}</td><td>${F(s.vel)}</td><td>${F(s.sr, 1)}</td><td>${F(s.tempo)}</td><td>${F(s.sl)}</td><td class="dk"><b>${F(bk.dak)}</b></td></tr>`; }).join('')}
      </tbody></table></div>`;
  } else if ((P === 50 && pattern.length === 5) || (P === 25 && lengths <= 16)) {
    const free = r => r.rows.filter(q => q.isFree);
    const cats = free(a).map(r => `${r.prevD}–${r.d}`);
    body += section('Ταχύτητα, συχνότητα και μήκος κύκλου', 'Στην καθαρή κολύμβηση (15 μετά τον τοίχο ως 5 πριν τον επόμενο)' + (P === 25 ? ', σε κάθε μήκος' : '') + ': ταχύτητα = συχνότητα × μήκος κύκλου.');
    body += charts(cats, f => three(x, I, r => r[f], free), cats.length > 12 ? 2 : 1, legLines(x, cats), cats.length > 12 ? 146 : cats.length > 8 ? 160 : 170);
    body += section('Οι χεριές ανά τμήμα', '');
    body += `<div class="box"><table class="tbl${cats.length > 10 ? ' sm' : ''}"><thead><tr><th>Τμήμα</th>${I.im || I.relay ? '<th>Στυλ</th>' : ''}<th>Χρόνος</th><th>Ταχύτητα<br>m/s</th><th>Χεριές</th><th>Συχνότητα</th><th>Tempo<br>s/κύκλο</th><th>Μήκος<br>κύκλου</th><th>SI<br>m²/s</th><th>Δ ταχ.<br>${esc(I.pbShort)}</th></tr></thead><tbody>
      ${free(a).map(r => `<tr><td class="l">${r.prevD}–${r.d}</td>${I.im || I.relay ? `<td class="l">${esc(STYLE_NAME[r.style])}</td>` : ''}<td>${F(r.seg)}</td><td><b>${F(r.vel)}</b></td><td>${F(r.strokes, 1)}</td><td>${F(r.sr, 1)}</td><td>${F(r.tempo)}</td><td>${F(r.sl)}</td><td>${F(r.si)}</td><td>${chip(r.vel - b.byD[r.d].vel, 'higher')}</td></tr>`).join('')}
      </tbody></table></div>`;
  } else {
    const cats = a.blocks.map(bk => String(bk.to));
    body += section('Χεριές ανά 50άρι', 'Στην καθαρή κολύμβηση κάθε 50άρι. Ταχύτητα = συχνότητα × μήκος κύκλου.');
    body += charts(cats, f => three(x, I, bk => bk[f === 'vel' ? 'vClean' : f], r => r.blocks), cats.length > 16 ? 2 : 1, legLines(x, cats), 185);
    const per = I.relay ? I.D / 4 : I.D <= 800 ? 200 : 500;
    const parts = [];
    for (let s = 0; s < I.D; s += per) {
      const bl = a.blocks.filter(bk => bk.from >= s && bk.to <= s + per);
      parts.push({ s, e: s + per, v: avg(bl, 'vClean'), sr: avg(bl, 'sr'), tempo: avg(bl, 'tempo'), sl: avg(bl, 'sl'), si: avg(bl, 'si'), dak: avg(bl, 'dak') });
    }
    body += section(I.relay ? 'Μέσοι όροι ανά σκέλος' : `Μέσοι όροι ανά ${per}${I.u}`, '');
    body += `<div class="box"><table class="tbl"><thead><tr><th>Μέρος</th><th>Ταχύτητα m/s</th><th>Συχνότητα</th><th>Tempo s/κύκλο</th><th>Μήκος κύκλου</th><th>SI</th><th>ΔΑΚ</th></tr></thead><tbody>
      ${parts.map(p => `<tr><td class="l">${p.s}–${p.e}${I.relay ? ' · Σκέλος ' + (p.s / per + 1) : ''}</td><td><b>${F(p.v)}</b></td><td>${F(p.sr, 1)}</td><td>${F(p.tempo)}</td><td>${F(p.sl)}</td><td>${F(p.si)}</td><td class="dk"><b>${F(p.dak)}</b></td></tr>`).join('')}
      </tbody></table></div>`;
  }
  return page(I, 'Χεριές: συχνότητα, tempo, μήκος κύκλου', body, n, N);
}

// ---- start and finish (and the single turn of the 100 / the 50 of the 25 m pool)
function startTable(x, I) {
  const a = x.race.start, b = x.pb.start, c = x.ref.start;
  const bk = a.style === 'bk';
  const rows = [
    ['Αντίδραση (πρώτη κίνηση)', 'reaction', 's', 'lower', 2],
    [bk ? 'Ώθηση από τον τοίχο' : 'Χρόνος στον βατήρα', 'block', 's', 'lower', 2],
    ['Πτήση ως την είσοδο', 'flight', 's', 'none', 2],
    ['Απόσταση εισόδου (χέρια)', 'entryDist', I.uu.trim(), 'higher', 2],
    ['Ταχύτητα εισόδου (χέρια)', 'entryVel', 'm/s', 'higher', 2],
    [a.style === 'br' ? 'Δελφινισμός στο υποβρύχιο' : 'Υποβρύχιοι δελφινισμοί', 'kicks', '', 'none', 0],
    ['Χρόνος ως την έξοδο', 'bt', 's', 'none', 2],
    ['Απόσταση εξόδου (κεφάλι)', 'bd', I.uu.trim(), 'higher', 2],
    ['Ταχύτητα είσοδος → έξοδος', 'entryBoVel', 'm/s', 'higher', 2],
    ['Ταχύτητα έξοδος → 15', 'bo15Vel', 'm/s', 'higher', 2],
    ['Χρόνος 15' + I.u, 't15', 's', 'lower', 2]
  ];
  return `<table class="tbl sm"><thead><tr><th>Εκκίνηση</th><th>Αγώνας</th><th>${esc(I.pbShort)}</th><th>Αναφορά</th><th>Δ ${esc(I.pbShort)}</th><th>Δ Αναφ.</th></tr></thead><tbody>
    ${rows.map(([lab, k, u, better, d]) => `<tr${k === 't15' ? ' class="tot"' : ''}><td class="l">${esc(lab)}</td><td><b>${F(a[k], d)}</b> <span class="dim">${esc(u)}</span></td><td>${F(b[k], d)}</td><td>${F(c[k], d)}</td><td>${chip(a[k] - b[k], better, d)}</td><td>${chip(a[k] - c[k], better, d)}</td></tr>`).join('')}
  </tbody></table>`;
}

function finishTiles(x, I) {
  const a = x.race.finish, b = x.pb.finish, c = x.ref.finish;
  return `<div class="tiles">${tile('Τελευταία 5' + I.u, T(a.last5), ' s', a.last5 - b.last5, a.last5 - c.last5, 'lower', 2, null, I)}
    ${tile('Τελ. χεριά → επαφή', F(a.lastGap), ' s', a.lastGap - b.lastGap, a.lastGap - c.lastGap, 'lower', 2, null, I)}
    ${tile('Ταχύτητα τελ. 5' + I.u, F(a.vLast5), ' m/s', a.vLast5 - b.vLast5, a.vLast5 - c.vLast5, 'higher', 2, null, I)}
    ${tile(`Τελευταία ${a.lastX}` + I.u, T(a.lastXt), ' s', a.lastXt - b.lastXt, a.lastXt - c.lastXt, 'lower', 2, null, I)}</div>`;
}

function pageStart(x, I, n, N) {
  const a = x.race;
  const tA = realTurns(a);
  let body = section(I.relay ? 'Εκκίνηση (1ο σκέλος)' : 'Εκκίνηση', 'Από το σήμα ως τα 15: χρόνοι από το σήμα, αποστάσεις από τον τοίχο της εκκίνησης.');
  body += `<div class="box">${ch.startStrip({ w: W - 26, s: a.start, fmt: v => F(v) }).replace(/ μ\.</g, I.yd ? ' yd<' : ' μ.<')}</div>`;
  body += `<div class="box">${startTable(x, I)}</div>`;
  const single = tA.length === 1 && !I.relay;
  if (single) {
    const t = tA[0], tb = realTurns(x.pb)[0], tc = realTurns(x.ref)[0];
    body += section(`Στροφή στα ${t.wall}${I.u}`, 'Από 5 πριν ως 15 μετά τον τοίχο.');
    body += `<div class="box">${ch.turnStrip({ w: W - 26, t: Object.assign({}, t, { bd: t.bd }), fmt: v => F(v) })}</div>`;
    body += `<div class="tiles">${tile('−5/+15', F(t.inOut), ' s', t.inOut - tb.inOut, t.inOut - tc.inOut, 'lower', 2, null, I)}
      ${tile('Στροφή', F(t.turnTime), ' s', t.turnTime - tb.turnTime, t.turnTime - tc.turnTime, 'lower', 2, t.kind === 'flip' ? 'τελευταία χεριά → πόδια φεύγουν' : 'επαφή → πόδια φεύγουν', I)}
      ${tile('Απόσταση εξόδου', F(t.bd, 1), I.u, t.bd - tb.bd, t.bd - tc.bd, 'higher', 1, `${t.kicks} δελφ. (${I.pbShort} ${tb.kicks}, αναφ. ${tc.kicks})`, I)}
      ${tile('Τοίχος → +15', F(t.out15), ' s', t.out15 - tb.out15, t.out15 - tc.out15, 'lower', 2, null, I)}</div>`;
  } else {
    const sp = st => [{ name: 'Βατήρας', value: st.block, color: '#123B8A' }, { name: 'Πτήση', value: st.flight, color: '#2E6BD6' },
      { name: 'Υποβρύχια', value: st.bt - st.entryT, color: '#5A8EDC' }, { name: 'Ως τα 15', value: st.t15 - st.bt, color: '#A9C4EE', ink: '#123B8A' }];
    body += section('Η εκκίνηση σε φάσεις', 'Ο χρόνος ως τα 15 στις φάσεις της εκκίνησης, δίπλα στο ' + I.pbShort + ' και την αναφορά.');
    body += `<div class="box">${ch.stacked({ w: W - 26, fmtT: v => F(v) + ' s', rows: [{ label: 'Αυτός ο αγώνας', parts: sp(a.start), total: a.start.t15 },
      { label: I.pbName, parts: sp(x.pb.start), total: x.pb.start.t15 }, { label: I.refName, parts: sp(x.ref.start), total: x.ref.start.t15 }] })}</div>`;
  }
  body += section(I.relay ? 'Τερματισμός (4ο σκέλος)' : 'Τερματισμός', 'Τα τελευταία μέτρα ως την επαφή.');
  body += finishTiles(x, I);
  return page(I, single ? 'Εκκίνηση, στροφή, τερματισμός' : 'Εκκίνηση και τερματισμός', body, n, N);
}

// ---- turns
function pageTurns(x, I, n, N) {
  const a = x.race, b = x.pb, c = x.ref;
  const tA = realTurns(a), tB = realTurns(b), tC = realTurns(c);
  const nT = tA.length;
  let body = section(`Στροφές (${nT})`, 'Από 5 πριν ως 15 μετά τον τοίχο, με τα γεγονότα στη σειρά που γίνονται. Στροφή: τελευταία χεριά → πόδια φεύγουν (τούμπα) ή επαφή → πόδια φεύγουν (ανοιχτή).');
  const kindCol = I.im || (I.relay && x.ev.style === 'im');
  const kindName = t => t.kind === 'change' ? `${STYLE_NAME[t.styleIn]} → ${STYLE_NAME[t.styleOut]}` : t.kind === 'flip' ? 'Τούμπα' : 'Ανοιχτή';
  if (nT <= 16) {
    body += `<div class="box"><table class="tbl${nT > 8 ? ' sm' : ''}"><thead><tr><th>Τοίχος</th>${kindCol ? '<th>Είδος</th>' : ''}<th>−5 →<br>τοίχος</th><th>Στροφή</th><th>Υποβρύχια</th><th>Δελφ.</th><th>Έξοδος</th><th>Τοίχος<br>→ +15</th><th>−5/+15</th><th>Δ ${esc(I.pbShort)}</th><th>Δ Αναφ.</th></tr></thead><tbody>
      ${tA.map((t, i) => `<tr><td class="l"><b>${t.wall}</b></td>${kindCol ? `<td class="l sm">${esc(kindName(t))}</td>` : ''}<td>${F(t.in5)}</td><td>${F(t.turnTime)}</td><td>${F(t.uw)}</td><td>${t.kicks}</td><td>${F(t.bd, 1)}</td><td>${F(t.out15)}</td><td><b>${F(t.inOut)}</b></td><td>${chip(t.inOut - tB[i].inOut, 'lower')}</td><td>${chip(t.inOut - tC[i].inOut, 'lower')}</td></tr>`).join('')}
      <tr class="tot"><td class="l">Μ.ο.</td>${kindCol ? '<td></td>' : ''}<td>${F(avg(tA, 'in5'))}</td><td>${F(avg(tA, 'turnTime'))}</td><td>${F(avg(tA, 'uw'))}</td><td>${F(avg(tA, 'kicks'), 1)}</td><td>${F(avg(tA, 'bd'), 1)}</td><td>${F(avg(tA, 'out15'))}</td><td><b>${F(avg(tA, 'inOut'))}</b></td><td>${chip(avg(tA, 'inOut') - avg(tB, 'inOut'), 'lower')}</td><td>${chip(avg(tA, 'inOut') - avg(tC, 'inOut'), 'lower')}</td></tr>
      </tbody></table></div>`;
  } else {
    const step = I.relay ? I.D / 4 : I.D <= 800 ? 100 : 200;
    const rows = [];
    for (let s = 0; s < I.D; s += step) {
      const pick = r => realTurns(r).filter(t => t.wall > s && t.wall <= s + step);
      const la = pick(a);
      if (!la.length) continue;
      rows.push({ s, e: s + step, n: la.length, inOut: avg(la, 'inOut'), in5: avg(la, 'in5'), out15: avg(la, 'out15'), bd: avg(la, 'bd'), kicks: avg(la, 'kicks'), turn: avg(la, 'turnTime'),
        dPb: avg(la, 'inOut') - avg(pick(b), 'inOut'), dRef: avg(la, 'inOut') - avg(pick(c), 'inOut') });
    }
    body += `<div class="box"><table class="tbl"><thead><tr><th>Μέρος</th><th>Στροφές</th><th>−5 → τοίχος</th><th>Στροφή</th><th>Δελφ.</th><th>Έξοδος</th><th>Τοίχος → +15</th><th>−5/+15</th><th>Δ ${esc(I.pbShort)}</th><th>Δ Αναφ.</th></tr></thead><tbody>
      ${rows.map(r => `<tr><td class="l">${r.s}–${r.e}</td><td>${r.n}</td><td>${F(r.in5)}</td><td>${F(r.turn)}</td><td>${F(r.kicks, 1)}</td><td>${F(r.bd, 1)}</td><td>${F(r.out15)}</td><td><b>${F(r.inOut)}</b></td><td>${chip(r.dPb, 'lower')}</td><td>${chip(r.dRef, 'lower')}</td></tr>`).join('')}
      <tr class="tot"><td class="l">Όλες</td><td>${nT}</td><td>${F(avg(tA, 'in5'))}</td><td>${F(avg(tA, 'turnTime'))}</td><td>${F(avg(tA, 'kicks'), 1)}</td><td>${F(avg(tA, 'bd'), 1)}</td><td>${F(avg(tA, 'out15'))}</td><td><b>${F(avg(tA, 'inOut'))}</b></td><td>${chip(avg(tA, 'inOut') - avg(tB, 'inOut'), 'lower')}</td><td>${chip(avg(tA, 'inOut') - avg(tC, 'inOut'), 'lower')}</td></tr>
      </tbody></table></div>`;
  }
  const cats = tA.map(t => String(t.wall));
  const every = nT > 32 ? 4 : nT > 16 ? 2 : 1;
  const ser = f => three(x, I, t => t[f], r => realTurns(r));
  const h = nT > 8 ? 175 : 190;
  body += section('Οι στροφές μέσα στον αγώνα', 'Πώς αλλάζουν από στροφή σε στροφή: ο χρόνος −5/+15 και η απόσταση εξόδου.');
  body += `<div class="box">${ch.lineChart({ w: W - 26, h, cats, every, fmt: v => v.toFixed(2), yTitle: '−5/+15 (s)', series: ser('inOut'), xTitle: 'τοίχος (' + I.uu.trim() + ')' })}</div>`;
  body += `<div class="box">${ch.lineChart({ w: W - 26, h, cats, every, fmt: v => v.toFixed(1), yTitle: 'Απόσταση εξόδου (' + I.uu.trim() + ')', series: ser('bd'), xTitle: 'τοίχος (' + I.uu.trim() + ')' })}</div>`;
  const oneKind = tA.every(t => t.kind === tA[0].kind && t.styleOut === tA[0].styleOut);
  if (nT <= 7 && oneKind) {
    const m = {};
    ['in5', 'out15', 'bd', 'turnTime', 'uw', 'vUw', 'vBo15', 'inOut'].forEach(k => { m[k] = avg(tA, k); });
    m.kicks = F(avg(tA, 'kicks'), 1);
    body += section('Η μέση στροφή του αγώνα', `Οι μέσοι όροι των ${nT} στροφών: −5/+15 σε ${F(m.inOut)} s (${I.pbShort} ${F(avg(tB, 'inOut'))}, αναφορά ${F(avg(tC, 'inOut'))}).`);
    body += `<div class="box">${ch.turnStrip({ w: W - 26, t: m, fmt: v => F(v) }).replace(/ μ\.</g, I.yd ? ' yd<' : ' μ.<')}</div>`;
  }
  return page(I, 'Στροφές', body, n, N);
}

// ---- IM: by style
function pageStyles(x, I, n, N) {
  const a = x.race, b = x.pb, c = x.ref, D = I.D, q = D / 4;
  const legs = IM_ORDER.map((st, i) => {
    const s = i * q, e = s + q, t = r => r.byD[e].t - (s === 0 ? 0 : r.byD[s].t);
    const fr = r => r.rows.filter(z => z.isFree && z.prevD >= s && z.d <= e);
    const w = (r, f) => { const l = fr(r); let cyc = 0, ct = 0, cd = 0; l.forEach(z => { cyc += z.sr * z.seg / 60; ct += z.seg; cd += (z.d - z.prevD) * r.k; }); return f === 'v' ? cd / ct : f === 'sr' ? cyc * 60 / ct : cd / cyc; };
    const bl = r => r.blocks.filter(bk => bk.from >= s && bk.to <= e);
    return { st, s, e, a: t(a), pb: t(b), ref: t(c), v: w(a, 'v'), sr: w(a, 'sr'), sl: w(a, 'sl'), vPb: w(b, 'v'), vRef: w(c, 'v'), dak: avg(bl(a), 'dak'), dakRef: avg(bl(c), 'dak') };
  });
  let body = section('Ανά στυλ', `Ο χρόνος κάθε ${q}${I.u} της μικτής και η διαφορά του από το ${I.pbShort} και την αναφορά.`);
  body += `<div class="tiles">${legs.map(l => tile(STYLE_NAME[l.st], T(l.a), ' s', l.a - l.pb, l.a - l.ref, 'lower', 2, `ΔΑΚ ${F(l.dak)} (αναφ. ${F(l.dakRef)})`, I)).join('')}</div>`;
  const share = r => IM_ORDER.map((st, i) => ({ name: STYLE_NAME[st], value: r.byD[(i + 1) * q].t - (i === 0 ? 0 : r.byD[i * q].t), color: ['#123B8A', '#2E6BD6', '#5A8EDC', '#A9C4EE'][i], ink: i === 3 ? '#123B8A' : undefined }));
  body += section('Πώς μοιράζεται ο χρόνος στα στυλ', '');
  body += `<div class="box">${ch.stacked({ w: W - 26, fmtT: v => T(v) + ' s', rows: [{ label: 'Αυτός ο αγώνας', parts: share(a), total: a.final }, { label: I.pbName, parts: share(b), total: b.final }, { label: I.refName, parts: share(c), total: c.final }] })}</div>`;
  body += section('Διαφορά από την αναφορά ανά στυλ', '');
  body += `<div class="box tight">${ch.hDiverging({ w: W - 26, items: legs.map(l => ({ label: `${STYLE_NAME[l.st]} (${l.s}–${l.e})`, value: l.a - l.ref, note: `${I.pbShort} ${S(l.a - l.pb)}` })), labelW: 190, noteW: 100, rowH: 27 })}</div>`;
  body += section('Κολύμβηση ανά στυλ', 'Στην καθαρή κολύμβηση κάθε στυλ.');
  body += `<div class="box"><table class="tbl"><thead><tr><th>Στυλ</th><th>Χρόνος</th><th>Ταχύτητα m/s</th><th>Δ ταχ. ${esc(I.pbShort)}</th><th>Δ ταχ. Αναφ.</th><th>Συχνότητα</th><th>Tempo</th><th>Μήκος κύκλου</th><th>ΔΑΚ</th></tr></thead><tbody>
    ${legs.map(l => `<tr><td class="l"><b>${esc(STYLE_NAME[l.st])}</b></td><td>${T(l.a)}</td><td><b>${F(l.v)}</b></td><td>${chip(l.v - l.vPb, 'higher')}</td><td>${chip(l.v - l.vRef, 'higher')}</td><td>${F(l.sr, 1)}</td><td>${F(60 / l.sr)}</td><td>${F(l.sl)}</td><td class="dk"><b>${F(l.dak)}</b></td></tr>`).join('')}
  </tbody></table></div>`;
  return page(I, 'Μικτή: ανά στυλ', body, n, N);
}

// ---- relays: legs and takeovers
function pageLegs(x, I, n, N) {
  const a = x.race, b = x.pb, c = x.ref;
  const pbs = legPbs(a), fb = FLY_BENEFIT[a.legLen] || 0.6;
  let body = section('Τα σκέλη', 'Ο χρόνος κάθε σκέλους (από την επαφή του προηγούμενου), δίπλα στο ατομικό ρεκόρ του κολυμβητή. Με αλλαγή αναμένεται περίπου ' + F(fb) + ' s πιο γρήγορα από το ατομικό ρεκόρ.');
  body += `<div class="box"><table class="tbl sm"><thead><tr><th>Σκέλος</th><th>Κολυμβητής</th><th>Στυλ</th><th>Χρόνος</th><th>Ατομικό<br>ρεκόρ</th><th>Δ</th><th>Αναμενόμενο</th><th>ΔΑΚ</th><th>Συχνότητα</th><th>Μήκος<br>κύκλου</th></tr></thead><tbody>
    ${a.legs.map((l, i) => { const expect = i === 0 ? pbs[i] : pbs[i] - fb; return `<tr><td class="l"><b>${i + 1}</b> · ${l.from}–${l.to}</td><td class="l">${esc(I.swimmers[i])}</td><td class="l">${esc(STYLE_NAME[l.style])}</td><td><b>${T(l.split)}</b></td><td>${T(pbs[i])}</td><td>${chip(l.split - pbs[i], 'lower')}</td><td>${T(expect)} ${chip(l.split - expect, 'lower')}</td><td class="dk"><b>${F(l.dak)}</b></td><td>${F(l.sr, 1)}</td><td>${F(l.sl)}</td></tr>`; }).join('')}
    <tr class="tot"><td class="l">Ομάδα</td><td></td><td></td><td><b>${T(a.final)}</b></td><td></td><td></td><td></td><td class="dk"><b>${F(a.avg.dak)}</b></td><td>${F(a.avg.sr, 1)}</td><td>${F(a.avg.sl)}</td></tr>
  </tbody></table></div>`;
  body += section('Κάθε σκέλος σε σχέση με το αναμενόμενο', 'Κάτω από το μηδέν πιο γρήγορα από το αναμενόμενο (μπλε), πάνω πιο αργά (πορτοκαλί).');
  body += `<div class="box">${ch.vDiverging({ w: W - 26, h: 140, cats: a.legs.map((l, i) => `Σκέλος ${i + 1}`), values: a.legs.map((l, i) => l.split - (i === 0 ? pbs[i] : pbs[i] - fb)), labels: true, yTitle: 's' })}</div>`;
  body += section('Οι αλλαγές', 'Επαφή του κολυμβητή που φτάνει → τα πόδια του επόμενου φεύγουν από τον βατήρα (επιτρέπεται ως −0.03 s). Ζώνη-στόχος 0.10–0.20 s.');
  body += `<div class="box"><table class="tbl"><thead><tr><th>Αλλαγή</th><th>Επαφή → πόδια<br>φεύγουν</th><th>${esc(I.pbShort)}</th><th>Αναφορά</th><th>15 από<br>την επαφή</th><th>Έξοδος</th><th>Δελφ.</th><th>Πάνω από 0.20 s</th></tr></thead><tbody>
    ${a.legs.slice(1).map((l, j) => { const t = a.turns.find(z => z.kind === 'exchange' && z.wall === l.from); return `<tr><td class="l">${j + 1} → ${j + 2} (${l.from}${I.u})</td><td><b>${F(l.ex)}</b> s</td><td>${F(b.legs[j + 1].ex)}</td><td>${F(c.legs[j + 1].ex)}</td><td>${F(l.out15)}</td><td>${F(t.bd, 1)}</td><td>${t.kicks}</td><td>${l.ex > 0.2 ? `<span class="chip l">${F(l.ex - 0.2)} s</span>` : '<span class="chip g">στη ζώνη</span>'}</td></tr>`; }).join('')}
    <tr class="tot"><td class="l">Σύνολο πάνω από 0.20</td><td colspan="7" class="l"><b>${F(a.legs.slice(1).reduce((s, l) => s + Math.max(0, l.ex - 0.2), 0))} s</b> · 1ο σκέλος: αντίδραση ${F(a.start.block)} s</td></tr>
  </tbody></table></div>`;
  body += `<div class="box">${ch.barChart({ w: W - 26, h: 132, cats: a.legs.slice(1).map((l, j) => `${j + 1} → ${j + 2}`), values: a.legs.slice(1).map(l => l.ex), fmt: v => F(v) + ' s', yTitle: 's',
    colors: a.legs.slice(1).map(l => l.ex > 0.2 ? C.lossMark : C.gainMark) })}</div>`;
  return page(I, 'Σκέλη και αλλαγές', body, n, N);
}

// ---- appendix
function appendixTable(x, I) {
  const a = x.race, b = x.pb, c = x.ref;
  const rows = a.rows;
  if (rows.length <= 20) {
    return `<div class="box"><table class="tbl sm"><thead><tr><th>Πέρασμα</th><th>Χρόνος</th><th>Τμήμα</th><th>m/s</th><th>Χεριές</th><th>Συχνότητα</th><th>Μήκος κύκλου</th><th>Δ ${esc(I.pbShort)}</th><th>Δ Αναφ.</th></tr></thead><tbody>
      ${rows.map(r => `<tr class="${r.isFree ? '' : 'phase'}"><td class="l"><b>${r.d}</b></td><td><b>${T(r.t)}</b></td><td>${F(r.seg)}</td><td>${F(r.vel)}</td><td>${r.isFree ? F(r.strokes, 1) : ''}</td><td>${r.sr ? F(r.sr, 1) : ''}</td><td>${r.sl ? F(r.sl) : ''}</td><td>${chip(r.t - b.byD[r.d].t, 'lower')}</td><td>${chip(r.t - c.byD[r.d].t, 'lower')}</td></tr>`).join('')}
      </tbody></table></div>`;
  }
  if (rows.length <= 66) {
    const cols = rows.length > 40 ? 3 : 2, per = Math.ceil(rows.length / cols);
    let html = '<div class="cols">';
    for (let k = 0; k < cols; k++) html += `<div class="box"><table class="tbl xs"><thead><tr><th>Πέρασμα</th><th>Χρόνος</th><th>Τμήμα</th>${cols === 2 ? '<th>m/s</th>' : ''}<th>Δ ${esc(I.pbShort)}</th><th>Δ Αναφ.</th></tr></thead><tbody>
      ${rows.slice(k * per, (k + 1) * per).map(r => `<tr class="${r.isFree ? '' : 'phase'}"><td class="l"><b>${r.d}</b></td><td><b>${T(r.t)}</b></td><td>${F(r.seg)}</td>${cols === 2 ? `<td>${F(r.vel)}</td>` : ''}<td>${chip(r.t - b.byD[r.d].t, 'lower')}</td><td>${chip(r.t - c.byD[r.d].t, 'lower')}</td></tr>`).join('')}</tbody></table></div>`;
    return html + '</div>';
  }
  // Long races in 25 pools: one row per length.
  const P = I.P, n = I.D / P;
  const len = [];
  for (let L = 0; L < n; L++) {
    const e = (L + 1) * P, s = L * P, t = r => r.byD[e].t - (s === 0 ? 0 : r.byD[s].t);
    const clean = a.rows.find(r => r.isFree && r.prevD === s + 15);
    len.push({ L: L + 1, e, cum: a.byD[e].t, split: t(a), v: clean ? clean.vel : null, sr: clean ? clean.sr : null, dPb: a.byD[e].t - b.byD[e].t, dRef: a.byD[e].t - c.byD[e].t });
  }
  const cols = 2, per = Math.ceil(len.length / cols);
  let html = '<div class="cols">';
  for (let k = 0; k < cols; k++) html += `<div class="box"><table class="tbl xs"><thead><tr><th>Μήκος</th><th>Σωρευτικός</th><th>Μήκος (s)</th><th>m/s 15–20</th><th>Συχν.</th><th>Δ ${esc(I.pbShort)}</th><th>Δ Αναφ.</th></tr></thead><tbody>
    ${len.slice(k * per, (k + 1) * per).map(r => `<tr><td class="l"><b>${r.L}</b> · ${r.e}</td><td><b>${T(r.cum)}</b></td><td>${F(r.split)}</td><td>${F(r.v)}</td><td>${F(r.sr, 1)}</td><td>${chip(r.dPb, 'lower')}</td><td>${chip(r.dRef, 'lower')}</td></tr>`).join('')}</tbody></table></div>`;
  return html + '</div>';
}

function cumChart(x, I, h) {
  const a = x.race, b = x.pb, c = x.ref;
  const pts = a.blocks.length <= 2 ? a.rows.map(r => r.d) : a.blocks.map(bk => bk.to);
  return section('Διαφορά χρόνου σε κάθε πέρασμα', 'Πόσο μπροστά (κάτω από το μηδέν) ή πίσω (πάνω) ήταν ' + (I.relay ? 'η ομάδα' : 'ο αθλητής') + ' σε κάθε σημείο του αγώνα.') +
    `<div class="box">${ch.lineChart({ w: W - 26, h, cats: pts.map(String), fmt: v => S(v), tickFmt: v => (v > 0 ? '+' : v < 0 ? '−' : '') + Math.abs(v).toFixed(1),
      refLine: { value: 0, label: '' }, yTitle: 's', every: pts.length > 24 ? 2 : 1, xTitle: 'απόσταση (' + I.uu.trim() + ')',
      series: [{ name: 'από το ' + I.pbShort, color: C.race, values: pts.map(d => a.byD[d].t - b.byD[d].t) }, { name: 'από την αναφορά', color: C.ref, dash: 'dot', marker: 'diamond', values: pts.map(d => a.byD[d].t - c.byD[d].t) }] })}</div>`;
}

function glossary(I) {
  const list = [
    ['ΔΑΚ', `ο βασικός δείκτης: (χεριές + δευτερόλεπτα από τα 15${I.u} ως τον τοίχο) ανά 10 μ., σε κάθε 50άρι· όσο μικρότερος, τόσο πιο αποδοτικό το κολύμπι.`],
    ['Καθαρή κολύμβηση', `τα τμήματα χωρίς εκκίνηση, στροφές και τερματισμό: από 15 μετά τον τοίχο ως 5 πριν τον επόμενο.`],
    ['Συχνότητα', 'κύκλοι χεριών ανά λεπτό (κύκλος = δύο χεριές στο ελεύθερο και στο ύπτιο, μία στην πεταλούδα και στο πρόσθιο).'],
    ['Tempo', 'δευτερόλεπτα ανά κύκλο χεριών (60 ÷ συχνότητα), όπως το δίνει ένα tempo trainer.'],
    ['Μήκος κύκλου', 'μέτρα ανά κύκλο χεριών· ταχύτητα = συχνότητα × μήκος κύκλου.'],
    ['SI', 'δείκτης κολύμβησης = ταχύτητα × μήκος κύκλου· μεγαλύτερος = πιο αποδοτική κολύμβηση.'],
    ['Στροφή −5/+15', 'ο χρόνος από 5 πριν ως 15 μετά τον τοίχο.'],
    I.relay ? ['Αλλαγή', 'επαφή του κολυμβητή που φτάνει → πόδια του επόμενου φεύγουν (ως −0.03 s επιτρέπεται).'] : ['Ρυθμός αγώνα', 'πώς μοιράζεται ο χρόνος στα 50άρια: σταθερός, θετικός (πιο αργό 2ο μισό), αρνητικός (πιο γρήγορο 2ο μισό).'],
    [I.pbShort, I.relay ? 'ο καλύτερος χρόνος της ομάδας, από αγώνα με ανάλυση.' : 'ατομικό ρεκόρ: ο καλύτερος χρόνος του αθλητή, από αγώνα με ανάλυση.'],
    ['Αναφορά', 'ο αγώνας σύγκρισης που διαλέγει ο αναλυτής (π.χ. ένας τελικός).']
  ];
  return section('Επεξήγηση όρων', '') + `<div class="gloss">${list.map(([k, v]) => `<div><b>${esc(k)}</b>: ${esc(v)}</div>`).join('')}</div>`;
}

function pagesAppendix(x, I, startN, N) {
  const a = x.race;
  const big = a.rows.length > 40 || (I.D <= 100 && !I.relay && a.rows.length > 10);
  const t1 = section(a.rows.length > 66 ? 'Ανά μήκος' : 'Όλα τα περάσματα', a.rows.length > 66 ? 'Ο σωρευτικός χρόνος σε κάθε τοίχο, ο χρόνος του μήκους και η ταχύτητα της καθαρής κολύμβησης (15–20).'
    : 'Ο χρόνος σε κάθε πέρασμα. Με απαλό πορτοκαλί τα τμήματα εκκίνησης, στροφής και τερματισμού.') + appendixTable(x, I);
  const small = I.D <= 100 && !I.relay;
  const phase = small ? section('Σύγκριση ανά φάση', '') + `<div class="box">${phaseTable(x, I)}</div>` : '';
  if (!big) return [page(I, 'Περάσματα, σύγκριση, όροι', t1 + phase + cumChart(x, I, 190) + glossary(I), startN, N)];
  if (small) return [page(I, 'Περάσματα και φάσεις', t1 + phase, startN, N), page(I, 'Σύγκριση και όροι', cumChart(x, I, 250) + glossary(I), startN + 1, N)];
  return [page(I, a.rows.length > 66 ? 'Ανά μήκος' : 'Περάσματα', t1, startN, N), page(I, 'Σύγκριση και όροι', phase + cumChart(x, I, 230) + glossary(I), startN + 1, N)];
}

// ---- the document
function plan(x, I) {
  const tA = realTurns(x.race);
  const P = [];
  P.push((R, n, N) => pageOverview(x, I, R, n, N));
  P.push((R, n, N) => pageRecs(x, I, R, n, N));
  P.push((R, n, N) => pageDak(x, I, n, N));
  if (I.relay) P.push((R, n, N) => pageLegs(x, I, n, N));
  if (I.D <= 100 && !I.relay) P.push((R, n, N) => pageSpeed(x, I, n, N));
  else P.push((R, n, N) => pageComposition(x, I, n, N));
  if (I.im && !I.relay) P.push((R, n, N) => pageStyles(x, I, n, N));
  if (I.D >= 200 || I.relay) P.push((R, n, N) => pagePace(x, I, R, n, N));
  P.push((R, n, N) => pageStrokes(x, I, n, N));
  P.push((R, n, N) => pageStart(x, I, n, N));
  if (tA.length >= 2 || (I.relay && tA.length >= 1)) P.push((R, n, N) => pageTurns(x, I, n, N));
  return P;
}

function document(ev) {
  const x = event(ev);
  const I = info(x);
  const R = recommendations(x);
  const P = plan(x, I);
  const extra = x.race.rows.length > 40 || (I.D <= 100 && !I.relay && x.race.rows.length > 10) ? 2 : 1;
  const N = P.length + extra;
  const pages = P.map((f, i) => f(R, i + 1, N));
  pagesAppendix(x, I, P.length + 1, N).forEach(p => pages.push(p));
  const dakAppendix = '';
  return { html: `<!doctype html><html lang="el"><head><meta charset="utf-8"><title>${esc(I.title)} · ${esc(I.pool)} · Πρότυπο αναφοράς</title><style>${CSS}</style></head><body>${pages.join('\n')}${dakAppendix}</body></html>`, I, x, R, pages: N };
}

const CSS = `
@page { size: A4; margin: 0; }
* { box-sizing: border-box; }
html, body { margin: 0; padding: 0; background: #fff; }
body { font-family: 'Inter', 'DejaVu Sans', sans-serif; color: ${C.text}; font-size: 11.2px; line-height: 1.38; -webkit-print-color-adjust: exact; print-color-adjust: exact;
  font-variant-numeric: tabular-nums; font-feature-settings: 'tnum' 1, 'cv11' 1; }
.page { width: 794px; height: 1123px; position: relative; overflow: hidden; page-break-after: always; break-after: page; padding: 0 34px; background: #fff; }
.band { margin: 0 -34px; background: ${C.ink}; color: #fff; padding: 18px 34px 20px; position: relative; }
.band::after { content: ''; position: absolute; left: 0; right: 0; bottom: 0; height: 4px; background: linear-gradient(90deg, #00C8FF, #2E6BD6 60%, #E65100); }
.band-top { display: flex; justify-content: space-between; align-items: center; margin-bottom: 10px; }
.kicker { font-size: 10px; letter-spacing: 0.14em; color: #8CA0B6; font-weight: 600; }
.proto { font-size: 9.5px; letter-spacing: 0.08em; color: #0B111A; background: #00C8FF; padding: 3px 9px; border-radius: 3px; font-weight: 800; }
.band-main { display: flex; justify-content: space-between; align-items: flex-end; gap: 16px; }
.event { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 33px; font-weight: 800; letter-spacing: -0.015em; line-height: 1.05; }
.event .cat { font-size: 16px; font-weight: 500; color: #8CA0B6; margin-left: 6px; }
.name { font-size: 18px; font-weight: 700; margin-top: 8px; }
.meta { font-size: 10.6px; color: #9DB0C4; margin-top: 4px; }
.hero { text-align: right; min-width: 250px; }
.hero-lab { font-size: 10px; letter-spacing: 0.12em; color: #8CA0B6; text-transform: uppercase; font-weight: 700; }
.hero-time { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 60px; font-weight: 800; letter-spacing: -0.02em; line-height: 1.0; margin: 3px 0 6px; }
.hero-sub { display: flex; gap: 12px; justify-content: flex-end; font-size: 10.6px; color: #C9D6E3; } .hero-sub span { white-space: nowrap; }
.band .chip.g { background: #1C55B8; color: #fff; } .band .chip.l { background: #E57B22; color: #0B111A; }
.heros { display: grid; grid-template-columns: 1.15fr 1fr 1fr; gap: 10px; margin: 14px 0 2px; }
.hc { border-radius: 12px; padding: 9px 14px 8px; background: #F4F7FB; border: 1px solid #E3E9F0; }
.hc.dak { background: linear-gradient(135deg, #FFF3E0, #FFE1C2); border-color: #F7C99A; }
.hc.aim { background: linear-gradient(135deg, #E9F6F4, #D5EEEA); border-color: #A9DCD3; }
.hc-lab { font-size: 10px; letter-spacing: 0.08em; text-transform: uppercase; font-weight: 800; color: #5B6B7B; }
.hc.dak .hc-lab { color: #B34700; } .hc.aim .hc-lab { color: #0E6B62; }
.hc-val { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 38px; font-weight: 800; color: ${C.ink}; line-height: 1.05; margin: 3px 0 3px; }
.hc.dak .hc-val { color: #B34700; font-size: 44px; }
.page.p1 .sec { margin: 10px 0 6px; }
.hc-u { font-size: 18px; font-weight: 600; color: #5B6B7B; }
.hc-sub { font-size: 11px; color: ${C.text}; } .hc-x { font-size: 10px; color: #5B6B7B; margin-top: 3px; }
.ph { display: flex; justify-content: space-between; align-items: center; padding: 16px 0 9px; border-bottom: 2.5px solid ${C.ink}; margin-bottom: 2px; }
.ph-l { font-size: 11px; color: ${C.dim}; } .ph-ev { font-weight: 800; color: ${C.ink}; } .ph-sep { margin: 0 6px; color: #B8C3CF; }
.ph-r { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 15px; font-weight: 800; color: ${C.ink}; }
.pf { position: absolute; left: 34px; right: 34px; bottom: 13px; display: flex; justify-content: space-between; font-size: 9px; color: #8A97A6; border-top: 1px solid #E3E8EE; padding-top: 6px; }
.sec { margin: 13px 0 7px; }
.sec-t { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 17px; font-weight: 800; color: ${C.navy}; display: flex; align-items: center; gap: 9px; }
.sec-t::before { content: ''; width: 5px; height: 17px; background: #00C8FF; border-radius: 3px; }
.sec-s { font-size: 10.6px; color: ${C.dim}; margin-top: 2px; max-width: 720px; }
.box { background: #fff; border: 1px solid #E3E8EE; border-radius: 10px; padding: 10px 12px; margin-bottom: 8px; }
.box.tight { padding: 6px 12px; }
.tiles { display: grid; grid-template-columns: repeat(4, 1fr); gap: 8px; margin-bottom: 6px; }
.tile { background: #F5F7FA; border: 1px solid #E6EBF1; border-radius: 10px; padding: 9px 11px 8px; min-width: 0; }
.t-lab { font-size: 10px; letter-spacing: 0.05em; text-transform: uppercase; color: ${C.dim}; font-weight: 700; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.t-val { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 27px; font-weight: 800; color: ${C.ink}; margin: 3px 0 4px; white-space: nowrap; }
.t-u { font-family: 'Inter', sans-serif; font-size: 11px; font-weight: 500; color: ${C.dim}; margin-left: 2px; }
.t-word { font-size: 17px; font-weight: 800; white-space: normal; display: inline-block; line-height: 1.12; }
.t-d { display: flex; justify-content: space-between; font-size: 10px; color: ${C.dim}; gap: 4px; }
.t-x { font-size: 10px; color: ${C.dim}; margin-top: 3px; }
.chip { display: inline-block; font-size: 10px; font-weight: 800; padding: 1px 6px; border-radius: 9px; line-height: 1.35; white-space: nowrap; }
.chip.g { background: #E1EAFB; color: #1C55B8; } .chip.l { background: #FCE6D3; color: #A24A07; } .chip.n { background: #EDF0F4; color: #5B6B7B; }
.sc { width: 100%; border-collapse: collapse; }
.sc th { font-size: 9.8px; font-weight: 700; color: #2B3F55; text-align: right; padding: 3px 6px 6px; border-bottom: 1.5px solid #D3DCE6; }
.sc th:first-child { text-align: left; } .sc th.bh { text-align: center; font-weight: 600; color: ${C.dim}; }
.kk { margin-right: 6px; font-weight: 600; color: ${C.text}; }
.k { display: inline-block; width: 9px; height: 9px; border-radius: 50%; margin: 0 3px 0 6px; vertical-align: -1px; }
.k.a { background: ${C.race}; } .k.p { background: ${C.pb}; border-radius: 2px; width: 4px; } .k.r { background: ${C.ref}; border-radius: 2px; width: 4px; }
.sc .k { display: inline-block; width: 9px; height: 9px; border-radius: 50%; margin: 0 3px 0 8px; vertical-align: -1px; }
.sc .k.a { background: ${C.race}; } .sc .k.p { background: ${C.pb}; border-radius: 2px; width: 4px; } .sc .k.r { background: ${C.ref}; border-radius: 2px; width: 4px; }
.sc td { padding: 2.5px 6px; border-bottom: 1px solid #EEF2F6; text-align: right; font-size: 11.5px; }
.sc td.l { text-align: left; font-weight: 600; font-size: 11.8px; } .sc td.b { width: 286px; text-align: center; }
.sc td.v b { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 19px; font-weight: 800; color: ${C.ink}; } .sc td .u { font-size: 10px; color: ${C.dim}; margin-left: 2px; }
.sc tr.main td { background: #FFF6EC; } .sc tr.main td.v b { color: #B34700; font-size: 22px; } .sc .sub { font-size: 9.8px; font-weight: 500; color: ${C.dim}; }
.msgs { display: grid; gap: 7px; }
.msg { display: flex; gap: 11px; align-items: flex-start; background: #F4F7FB; border-radius: 10px; padding: 6px 12px; font-size: 12.2px; line-height: 1.38; }
.msg .num { flex: 0 0 26px; height: 26px; border-radius: 50%; background: ${C.ink}; color: #fff; font-weight: 800; font-size: 13px; display: flex; align-items: center; justify-content: center; }
.aim-card { display: flex; gap: 18px; align-items: center; background: linear-gradient(135deg, #0B111A, #14243A); color: #fff; border-radius: 12px; padding: 13px 18px; margin-bottom: 10px; }
.ac-lab { font-size: 10px; letter-spacing: 0.1em; text-transform: uppercase; color: #8CA0B6; font-weight: 800; }
.ac-val { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 44px; font-weight: 800; color: #5FE3C9; line-height: 1.05; }
.ac-txt { font-size: 12.2px; color: #DCE6F0; line-height: 1.45; } .ac-txt b { color: #fff; }
.rc { border: 1px solid #E3E8EE; border-left: 5px solid #2E6BD6; border-radius: 10px; padding: 9px 13px 10px; margin-bottom: 8px; background: #fff; }
.rc:nth-of-type(1) { border-left-color: #E65100; }
.rc-h { display: flex; align-items: center; gap: 10px; margin-bottom: 6px; }
.rc-n { width: 26px; height: 26px; border-radius: 50%; background: ${C.navy}; color: #fff; font-weight: 800; display: flex; align-items: center; justify-content: center; font-size: 13px; }
.rc-t { font-family: 'Inter Display', 'Inter', sans-serif; font-size: 16px; font-weight: 800; color: ${C.ink}; flex: 1; }
.rc-g { font-size: 13px; font-weight: 800; color: #0E6B62; background: #DDF3EF; padding: 3px 10px; border-radius: 12px; } .rc-g.n { color: #5B6B7B; background: #EDF0F4; }
.rc-b { display: grid; grid-template-columns: 1.05fr 1fr; gap: 16px; }
.rc-l { font-size: 9.8px; letter-spacing: 0.07em; text-transform: uppercase; color: ${C.dim}; font-weight: 800; margin: 2px 0 3px; }
.rc ul { margin: 0; padding-left: 15px; } .rc li { font-size: 11.2px; margin: 2px 0; line-height: 1.38; }
.rc-a { font-size: 11.6px; line-height: 1.42; margin-bottom: 5px; }
.tbl { width: 100%; border-collapse: collapse; font-size: 11px; }
.tbl th { background: #E8EEF5; color: #2B3F55; font-weight: 700; font-size: 9.8px; padding: 4px 5px; text-align: right; line-height: 1.2; }
.tbl th:first-child { text-align: left; border-top-left-radius: 6px; } .tbl th:last-child { border-top-right-radius: 6px; }
.tbl td { padding: 3.6px 5px; text-align: right; border-bottom: 1px solid #EEF2F6; white-space: nowrap; }
.tbl td.l { text-align: left; } .tbl td.dk { background: #FFF6EC; color: #B34700; } .tbl td.aimv { background: #EAF7F4; color: #0E6B62; }
.tbl tr.phase td { background: #FDF4EA; }
.tbl tr.tot td { background: #F1F5F9; font-weight: 600; border-top: 1px solid #D3DCE6; } .tbl tr.tot td b { font-weight: 800; }
.tbl.sm { font-size: 10.4px; } .tbl.sm td { padding: 2.8px 4px; } .tbl.sm th { padding: 3px 4px; font-size: 9.4px; }
.tbl.xs { font-size: 9.6px; } .tbl.xs td { padding: 1.9px 3.5px; } .tbl.xs th { padding: 3px 3.5px; font-size: 8.8px; }
.tbl .chip { font-size: 9.2px; padding: 0 4px; }
.tbl th.g1 { background: #DDE6F6; color: #123B8A; text-align: center; } .tbl th.g2 { background: #E7EFFB; color: #2B5BA8; text-align: center; }
.tbl td.sm { font-size: 9.6px; }
.dim { color: #8A97A6; }
.lgrow { display: flex; gap: 16px; margin: 6px 0 0 96px; font-size: 10.5px; color: ${C.text}; }
.lgrow.c { margin: -2px 0 6px 0; justify-content: center; }
.lgi { display: inline-flex; align-items: center; gap: 5px; } .lgi i { width: 12px; height: 10px; border-radius: 2px; display: inline-block; }
.lgi b.ln { width: 22px; height: 0; border-top: 2.5px solid; display: inline-block; } .lgi b.ln.dash { border-top-style: dashed; } .lgi b.ln.dot { border-top-style: dotted; }
.two { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; }
.cols { display: grid; grid-template-columns: repeat(auto-fit, minmax(0, 1fr)); gap: 8px; } .cols .box { padding: 6px 6px; }
.ins { background: #F1F5FD; border-left: 4px solid #1C55B8; border-radius: 8px; padding: 8px 13px; margin: 2px 0 8px; }
.ins.dakx { background: #FFF6EC; border-left-color: #E65100; }
.ins-t { font-family: 'Inter Display', 'Inter', sans-serif; font-weight: 800; font-size: 12.5px; color: ${C.ink}; margin-bottom: 3px; }
.ins ul { margin: 0; padding-left: 15px; } .ins li { margin: 2px 0; font-size: 11px; }
.gloss { display: grid; grid-template-columns: 1fr 1fr; gap: 5px 18px; font-size: 10.4px; color: ${C.text}; line-height: 1.42; }
svg.chart { display: block; overflow: visible; }
svg text { font-family: 'Inter', 'DejaVu Sans', sans-serif; }
.ax { font-size: 10px; fill: ${C.dim}; } .lg { font-size: 10.3px; fill: ${C.text}; } .pl { font-size: 9.6px; fill: ${C.text}; font-weight: 700; }
.el { font-size: 10.4px; } .an { font-size: 9.6px; fill: ${C.dim}; } .tl { font-size: 11px; fill: ${C.text}; } .pv { font-size: 10.4px; font-weight: 800; }
.nt { font-size: 10px; fill: ${C.dim}; } .sb { font-size: 10.2px; fill: #fff; font-weight: 800; } .sv { font-size: 9.8px; fill: #fff; }
.tv { font-size: 12.5px; font-weight: 800; fill: ${C.ink}; } .zn { font-size: 11px; fill: ${C.navy}; font-weight: 700; } .et { font-size: 11.5px; font-weight: 800; fill: ${C.ink}; }
.es { font-size: 10px; fill: ${C.dim}; } .sv2 { font-size: 11px; font-weight: 800; fill: ${C.text}; }
.bx { font-size: 8px; fill: #9AA6B4; }
.hs { font-size: 9.6px; fill: ${C.dim}; font-weight: 600; } .hv { font-size: 15px; font-weight: 800; fill: ${C.ink}; } .hd { font-size: 10px; fill: #8A3A00; font-weight: 700; }
.hs.s { font-size: 8.6px; } .hv.s { font-size: 12.5px; } .hd.s { font-size: 8.8px; }
`;

module.exports = { document };
