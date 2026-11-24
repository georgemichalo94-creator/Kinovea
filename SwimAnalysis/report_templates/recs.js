// Recommendations from the numbers of a race: what the numbers show, what to do, the target, the estimated gain.
'use strict';
const { STYLE_NAME } = require('./sim2');

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
const avg = (l, f) => l.reduce((s, t) => s + (typeof f === 'function' ? f(t) : t[f]), 0) / Math.max(1, l.length);
const sum = (l, f) => l.reduce((s, t) => s + (typeof f === 'function' ? f(t) : t[f]), 0);
const U = ev => ev.unit === 'yd' ? ' yd' : ' μ.';
const FLY_BENEFIT = { 50: 0.45, 100: 0.60, 200: 0.75 };
const IN_STYLE = { fly: 'στην πεταλούδα', bk: 'στο ύπτιο', br: 'στο πρόσθιο', fs: 'στο ελεύθερο' };
const kicksText = n => n <= 1 ? 'ένας δελφινισμός' : n + ' δελφινισμοί';
const end = t => /[.!]$/.test(t.replace(/<[^>]+>/g, '')) ? t : t + '.';

/** The individual best times of the relay swimmers (fictional): the 1st leg from a flat start, the others with the takeover. */
function legPbs(a) {
  if (!a.legs) return null;
  const u = [-0.10, 0.35, -0.05, 0.10];
  const fb = FLY_BENEFIT[a.legLen] || 0.6;
  return a.legs.map((l, i) => i === 0 ? l.split + u[0] : l.split + fb - u[i]);
}

function recommendations(x) {
  const ev = x.ev, a = x.race, b = x.pb, c = x.ref, D = ev.distance, u = U(ev);
  const relay = ev.relay, im = ev.style === 'im';
  const recs = [];
  const real = r => r.turns.filter(t => t.kind !== 'exchange');
  const tA = real(a), tB = real(b), tC = real(c);
  const who = relay ? 'καλύτερος χρόνος ομάδας' : 'ΑΡ';

  // 1. Turns.
  if (tA.length && im) {
    const gap = sum(tA, 'inOut') - sum(tC, 'inOut');
    if (gap > 0.08) {
      const worst = tA.map((t, i) => ({ t, d: t.inOut - tC[i].inOut, c: tC[i] })).sort((p, q) => q.d - p.d)[0];
      const name = t => t.styleIn === t.styleOut ? `στροφή ${IN_STYLE[t.styleIn]}` : `${STYLE_NAME[t.styleIn].toLowerCase()} → ${STYLE_NAME[t.styleOut].toLowerCase()}`;
      const changes = tA.filter(t => t.kind === 'change').length;
      recs.push({ key: 'turn', title: changes === tA.length ? 'Αλλαγές στυλ' : changes ? 'Στροφές και αλλαγές στυλ' : `Στροφές (${tA.length})`, facts: [
        `−5/+15 κατά μ.ο. <b>${F(avg(tA, 'inOut'))} s</b> (${who} ${F(avg(tB, 'inOut'))}, αναφορά ${F(avg(tC, 'inOut'))})`,
        `η μεγαλύτερη διαφορά στα ${worst.t.wall}${u} (${name(worst.t)}): <b>${S(worst.d)} s</b> από την αναφορά`,
        `εκεί: επαφή → πόδια φεύγουν ${F(worst.t.turnTime)} s (αναφ. ${F(worst.c.turnTime)}) · έξοδος στα ${F(worst.t.bd, 1)}${u} (αναφ. ${F(worst.c.bd, 1)})`],
        action: (worst.t.styleIn === worst.t.styleOut ? `Προπόνηση της στροφής ${IN_STYLE[worst.t.styleIn]}` : `Προπόνηση της αλλαγής ${name(worst.t)}`) +
          (worst.t.turnTime - worst.c.turnTime > 0.08 ? ': πιο γρήγορη περιστροφή από την επαφή ως την ώθηση.' : ': μεγαλύτερη και πιο γρήγορη υποβρύχια φάση μετά την ώθηση.'),
        target: `−5/+15 ≤ <b>${F(avg(tA, 'inOut') - (avg(tA, 'inOut') - avg(tC, 'inOut')) / 2)} s</b> κατά μ.ο.`, gain: gap / 2 });
    }
  } else if (tA.length) {
    const gap = sum(tA, 'inOut') - sum(tC, 'inOut');
    if (gap > 0.08) {
      const outGap = avg(tA, 'out15') - avg(tC, 'out15'), inGap = avg(tA, 'in5') - avg(tC, 'in5');
      const bdA = avg(tA, 'bd'), bdC = avg(tC, 'bd'), kA = avg(tA, 'kicks'), kC = avg(tC, 'kicks');
      const trend = tA.length >= 3 ? avg(tA.slice(-2), 'inOut') - avg(tA.slice(0, 2), 'inOut') : 0;
      const flip = tA.some(t => t.kind === 'flip');
      const facts = [
        `−5/+15 κατά μ.ο. <b>${F(avg(tA, 'inOut'))} s</b> (${who} ${F(avg(tB, 'inOut'))}, αναφορά ${F(avg(tC, 'inOut'))})`,
        `έξοδος στα <b>${F(bdA, 1)}${u}</b> με ${F(kA, 1)} δελφινισμούς (αναφορά ${F(bdC, 1)}${u}, ${F(kC, 1)})`,
        `−5 → τοίχος ${F(avg(tA, 'in5'))} s (αναφ. ${F(avg(tC, 'in5'))}) · τοίχος → +15 ${F(avg(tA, 'out15'))} s (αναφ. ${F(avg(tC, 'out15'))})`
      ];
      if (trend > 0.2) facts.push(`οι στροφές του τέλους είναι <b>${F(trend)} s</b> πιο αργές από τις πρώτες`);
      const moreKicks = Math.max(1, Math.round(kC - kA));
      const bdT = Math.round((bdA + bdC) / 2 * 2) / 2;
      let action = outGap >= inGap
        ? (tA[0].styleOut === 'br' ? `Μεγαλύτερο και πιο γρήγορο υποβρύχιο μετά τον τοίχο (pull-out), έξοδος στα ${F(bdT, 1)}${u} και γρήγορη πρώτη χεριά.`
          : `Μεγαλύτερη υποβρύχια φάση μετά τον τοίχο: ${kicksText(moreKicks)} ακόμη, έξοδος στα ${F(bdT, 1)}${u} και γρήγορη πρώτη χεριά.`)
        : `Πιο γρήγορη προσέγγιση: χωρίς γλίστρημα στα τελευταία μέτρα και σωστή απόσταση για την τελευταία χεριά πριν ${flip ? 'την τούμπα' : 'την επαφή'}.`;
      if (trend > 0.2) action += ' Στροφές και στο τέλος των σετ, όταν ο αθλητής είναι κουρασμένος.';
      const target = avg(tA, 'inOut') - (avg(tA, 'inOut') - avg(tC, 'inOut')) / 2;
      recs.push({ key: 'turn', title: tA.length > 1 ? `Στροφές (${tA.length})` : 'Στροφή', facts, action,
        target: `−5/+15 ≤ <b>${F(target)} s</b> κατά μ.ο. · έξοδος ≥ <b>${F(bdT, 1)}${u}</b>`, gain: gap / 2 });
    }
  }

  // 2. Start.
  const s = a.start, sc = c.start, sb = b.start;
  const sGap = s.t15 - sc.t15;
  if (sGap > 0.05) {
    const blockLoss = s.block - sc.block, entryLoss = sc.entryDist - s.entryDist, bdLoss = sc.bd - s.bd;
    const facts = [
      `15${u} σε <b>${F(s.t15)} s</b> (${who} ${F(sb.t15)}, αναφορά ${F(sc.t15)})`,
      `${s.style === 'bk' ? 'ώθηση από τον τοίχο' : 'βατήρας'} ${F(s.block)} s (αναφ. ${F(sc.block)}) · είσοδος στα ${F(s.entryDist)}${u} (αναφ. ${F(sc.entryDist)})`,
      `έξοδος στα <b>${F(s.bd, 1)}${u}</b>, ${s.kicks} δελφινισμοί (αναφ. ${F(sc.bd, 1)}${u}, ${sc.kicks})`
    ];
    let action;
    if (bdLoss >= 0.8) action = end(`Μεγαλύτερη υποβρύχια φάση: ${s.style === 'br' ? 'πιο μακρύ και πιο γρήγορο υποβρύχιο (pull-out)' : kicksText(Math.max(1, sc.kicks - s.kicks)) + ' ακόμη'}, έξοδος στα ${F(Math.round((s.bd + sc.bd) / 2 * 2) / 2, 1)}${u}`);
    else if (blockLoss >= 0.03) action = `Πιο γρήγορη αντίδραση στο σήμα: εκκινήσεις με ήχο, στόχος ${s.style === 'bk' ? 'ώθηση' : 'βατήρας'} ≤ ${F(s.block - blockLoss / 2)} s.`;
    else if (entryLoss >= 0.15) action = end(`Πιο μακριά είσοδος: δυνατότερη ώθηση, είσοδος στα ${F(s.entryDist + entryLoss / 2)}${u}`);
    else action = 'Πιο γρήγορη μετάβαση από την υποβρύχια φάση στην πρώτη χεριά.';
    recs.push({ key: 'start', title: 'Εκκίνηση', facts, action, target: `15${u} σε ≤ <b>${F(s.t15 - sGap / 2)} s</b>`, gain: sGap / 2 });
  }

  // 3. Swimming and ΔΑΚ.
  const swGap = a.composition.swim - c.composition.swim;
  if (swGap > 0.1) {
    const slGap = c.avg.sl - a.avg.sl, srGap = c.avg.sr - a.avg.sr;
    const facts = [
      `ΔΑΚ κατά μ.ο. <b>${F(a.avg.dak)}</b> (${who} ${F(b.avg.dak)}, αναφορά ${F(c.avg.dak)})`,
      `ταχύτητα κολύμβησης <b>${F(a.avg.vClean)} m/s</b> (αναφ. ${F(c.avg.vClean)})`,
      `μήκος κύκλου ${F(a.avg.sl)} μ. (αναφ. ${F(c.avg.sl)}) · συχνότητα ${F(a.avg.sr, 1)} (αναφ. ${F(c.avg.sr, 1)}) · tempo ${F(a.avg.tempo)} s/κύκλο`
    ];
    let action, target;
    const dakT = a.avg.dak - (a.avg.dak - c.avg.dak) / 2;
    if (slGap >= 0.03 && slGap / c.avg.sl >= srGap / c.avg.sr) {
      action = 'Μεγαλύτερο μήκος κύκλου στην ίδια συχνότητα: σετ με μέτρηση χεριών και χρόνου σε κάθε 50άρι (ΔΑΚ).';
      target = `μήκος κύκλου ≥ <b>${F(a.avg.sl + slGap / 2)} μ.</b> · ΔΑΚ ≤ <b>${F(dakT)}</b>`;
    } else if (srGap >= 1.5) {
      action = `Πιο γρήγορο tempo χεριών με το ίδιο μήκος: tempo trainer στα ${F(60 / (a.avg.sr + srGap / 2))} s/κύκλο (τώρα ${F(a.avg.tempo)}).`;
      target = `συχνότητα ≥ <b>${F(a.avg.sr + srGap / 2, 1)}</b> κύκλοι/λεπτό · ΔΑΚ ≤ <b>${F(dakT)}</b>`;
    } else {
      action = 'Ταχύτητα κολύμβησης στον ρυθμό του αγώνα, με μέτρηση του ΔΑΚ σε κάθε επανάληψη.';
      target = `ταχύτητα ≥ <b>${F(a.avg.vClean + (c.avg.vClean - a.avg.vClean) / 2)} m/s</b> · ΔΑΚ ≤ <b>${F(dakT)}</b>`;
    }
    recs.push({ key: 'swim', title: 'Κολύμβηση και ΔΑΚ', facts, action, target, gain: swGap / 2 });
  }

  // 4. ΔΑΚ through the race (the second half against the first).
  const bl = a.blocks;
  if (bl.length >= 4 && !im) {
    const h = Math.floor(bl.length / 2), first = bl.slice(0, h), second = bl.slice(h);
    const rise = avg(second, 'dak') - avg(first, 'dak');
    if (rise >= 0.3) {
      const rS = avg(second, 'dakStrokes') - avg(first, 'dakStrokes'), rT = avg(second, 'dakTime') - avg(first, 'dakTime');
      const fT = avg(first, 'dakTime');
      const save = second.reduce((acc, bk) => acc + Math.max(0, bk.dakTime - fT) * (bk.winTime / bk.dakTime), 0) / 2;
      recs.push({ key: 'dak', title: 'ΔΑΚ στο 2ο μισό', facts: [
        `ΔΑΚ 1ου μισού <b>${F(avg(first, 'dak'))}</b> → 2ου μισού <b>${F(avg(second, 'dak'))}</b> (${S(rise)})`,
        `από τις χεριές ${S(rS)} και από τον χρόνο ${S(rT)} ανά 10 μ.`,
        `ο υψηλότερος ΔΑΚ στο 50άρι ${bl.reduce((p, q) => q.dak > p.dak ? q : p).from}–${bl.reduce((p, q) => q.dak > p.dak ? q : p).to}${u} (${F(Math.max(...bl.map(q => q.dak)))})`],
        action: rS > rT ? 'Οι χεριές πληθαίνουν όταν κουράζεται: σετ αποδοτικότητας στο τέλος της προπόνησης, με το ίδιο μήκος κύκλου ως το τέλος.'
          : 'Ο χρόνος ανεβαίνει περισσότερο από τις χεριές: σετ ρυθμού στο 2ο μισό, με τον ΔΑΚ του 1ου μισού ως στόχο.',
        target: `ΔΑΚ 2ου μισού ≤ <b>${F(avg(first, 'dak') + rise / 2)}</b>`, gain: save > 0.05 ? save : null });
    }
  }

  // 5. Pacing (200 and longer, not the medley).
  let splits = null;
  if (bl.length >= 4 && !im) {
    const mean = r => avg(r.blocks.slice(1), 'time');
    const dev0 = bl[0].time - mean(a), dev0c = c.blocks[0].time - mean(c);
    const half = r => { const n = r.blocks.length / 2; return sum(r.blocks.slice(n), 'time') - sum(r.blocks.slice(0, n), 'time'); };
    const fade = half(a) - half(c);
    const facts = [`1ο 50άρι <b>${T(bl[0].time)}</b>, μ.ο. των υπολοίπων ${T(mean(a))} (αναφορά ${T(c.blocks[0].time)} / ${T(mean(c))})`,
      `2ο μισό ${S(half(a))} s από το 1ο (αναφορά ${S(half(c))})`];
    let action = null;
    if (dev0 - dev0c < -0.4) action = 'Πιο ελεγχόμενο 1ο 50άρι: λίγο πιο αργά στην αρχή, για να μείνει ταχύτητα για το τέλος.';
    else if (fade > 0.6) action = 'Κράτημα του ρυθμού στο 2ο μισό: επαναλήψεις στους χρόνους-στόχους με μικρά διαλείμματα.';
    else if (bl.length >= 6 && avg(bl.slice(-2), 'time') - avg(c.blocks.slice(-2), 'time') > avg(bl.slice(1, -2), 'time') - avg(c.blocks.slice(1, -2), 'time') + 0.2) action = 'Δυνατότερο τέλος: το τελευταίο 100άρι πιο γρήγορα από τον μέσο όρο του αγώνα.';
    if (action) recs.push({ key: 'pace', title: 'Ρυθμός αγώνα', facts, action, target: 'χρόνοι-στόχοι ανά 50άρι στη σελίδα του ρυθμού', gain: null });
  }

  // 6. Finish.
  const fGap = a.finish.last5 - c.finish.last5;
  if (fGap > 0.05) {
    const gapLoss = a.finish.lastGap - c.finish.lastGap;
    recs.push({ key: 'finish', title: 'Τερματισμός', facts: [
      `τελευταία 5${u} σε <b>${F(a.finish.last5)} s</b> (${who} ${F(b.finish.last5)}, αναφορά ${F(c.finish.last5)})`,
      `τελευταία χεριά → επαφή ${F(a.finish.lastGap)} s (αναφ. ${F(c.finish.lastGap)})`],
      action: gapLoss > 0.04 ? 'Η τελευταία χεριά να «κλείνει» στον τοίχο: τερματισμοί χωρίς γλίστρημα, με το κεφάλι κάτω.'
        : 'Δυνατότερα τελευταία μέτρα: τερματισμοί με γρήγορο ρυθμό στα τελευταία 10 μ.',
      target: `τελευταία 5${u} σε ≤ <b>${F(a.finish.last5 - fGap / 2)} s</b>`, gain: fGap / 2 });
  }

  // 7. Stroke rate in the second half (100 and longer, not the medley).
  const free = a.rows.filter(r => r.sr !== null);
  if (!im && free.length >= 4) {
    const n = free.length, early = free.slice(0, Math.max(1, Math.floor(n / 3))), late = free.slice(Math.floor(2 * n / 3), D >= 400 ? n - 2 : n);
    const drop = late.length ? avg(early, 'sr') - avg(late, 'sr') : 0;
    if (drop >= 1.8) recs.push({ key: 'rate', title: 'Συχνότητα στο τέλος', facts: [
      `συχνότητα στην αρχή <b>${F(avg(early, 'sr'), 1)}</b> → στο τέλος <b>${F(avg(late, 'sr'), 1)}</b> κύκλοι/λεπτό (${S(-drop, 1)})`,
      `tempo ${F(60 / avg(early, 'sr'))} → ${F(60 / avg(late, 'sr'))} s/κύκλο`],
      action: `Κράτημα της συχνότητας: σετ με tempo trainer στα ${F(60 / avg(early, 'sr'))} s/κύκλο στο τέλος των επαναλήψεων.`,
      target: `συχνότητα τέλους ≥ <b>${F(avg(late, 'sr') + drop / 2, 1)}</b>`, gain: null });
  }

  // 8. Relays: the takeovers and the legs.
  if (relay) {
    const pbs = legPbs(a);
    a.legs.slice(1).forEach((l, j) => {
      if (l.ex > 0.24) recs.push({ key: 'ex' + (j + 2), title: `Αλλαγή ${j + 1} → ${j + 2}`, facts: [
        `επαφή → πόδια φεύγουν <b>${F(l.ex)} s</b> (${who} ${F(b.legs[j + 1].ex)}, αναφορά ${F(c.legs[j + 1].ex)})`,
        `15${u} από την επαφή σε ${F(l.out15)} s (αναφ. ${F(c.legs[j + 1].out15)})`],
        action: 'Προπόνηση αλλαγών με τον ίδιο κολυμβητή που φτάνει: ξεκίνημα της κίνησης όταν το χέρι του είναι στα τελευταία 1–1.5 μ.',
        target: `αλλαγή σε <b>0.10–0.20 s</b>`, gain: l.ex - 0.18 });
    });
    a.legs.forEach((l, i) => {
      const d = l.split - pbs[i], expect = i === 0 ? 0 : -(FLY_BENEFIT[a.legLen] || 0.6);
      if (d - expect > 0.2) recs.push({ key: 'leg' + i, title: `Σκέλος ${i + 1} (${STYLE_NAME[l.style].toLowerCase()})`, facts: [
        `σκέλος <b>${T(l.split)}</b>, ατομικό ρεκόρ ${T(pbs[i])} (${S(d)} s)`,
        i === 0 ? 'με κανονική εκκίνηση: αναμένεται κοντά στο ατομικό ρεκόρ' : `με αλλαγή αναμένεται περίπου ${S(expect)} s από το ατομικό ρεκόρ`,
        `ΔΑΚ ${F(l.dak)} · συχνότητα ${F(l.sr, 1)} · μήκος κύκλου ${F(l.sl)} μ.`],
        action: 'Έλεγχος της σειράς των κολυμβητών και του ρυθμού του σκέλους: επαναλήψεις στον χρόνο-στόχο του σκέλους.',
        target: `σκέλος ≤ <b>${T(pbs[i] + expect)}</b>`, gain: (d - expect) / 2 });
    });
  }

  recs.sort((p, q) => (q.gain === null ? -1 : q.gain) - (p.gain === null ? -1 : p.gain));
  const gains = recs.filter(r => r.gain !== null).slice(0, 4).reduce((acc, r) => acc + r.gain, 0);
  const targetTime = a.final - gains;
  // Target splits per 50: the profile of the reference on the target time.
  if (bl.length >= 4 && !im) {
    splits = c.blocks.map(bk => bk.time * targetTime / c.final);
  }
  return { recs, gains, targetTime, splits };
}

/** The three messages of the first page. */
function messages(x, R) {
  const ev = x.ev, a = x.race, b = x.pb, c = x.ref, u = U(ev);
  const who = ev.relay ? 'τον καλύτερο χρόνο της ομάδας' : 'το ατομικό ρεκόρ';
  const out = [];
  const d = a.final - b.final;
  out.push(d < -0.005 ? `<b>${ev.relay ? 'Νέος καλύτερος χρόνος ομάδας' : 'Νέο ατομικό ρεκόρ'}: ${T(a.final)}</b>, ${S(d)} s από ${who}· ${S(a.final - c.final)} s από την αναφορά.`
    : `<b>${T(a.final)}</b>: ${S(d)} s από ${who} (${T(b.final)})· ${S(a.final - c.final)} s από την αναφορά.`);
  const bl = a.blocks;
  const dPbDak = a.avg.dak - b.avg.dak, dRefDak = a.avg.dak - c.avg.dak;
  const vsPb = dPbDak < -0.03 ? 'καλύτερος από' : dPbDak > 0.03 ? 'χειρότερος από' : 'ίδιος με';
  const vsRef = dRefDak < -0.03 ? `καλύτερος και από την αναφορά (${F(c.avg.dak)})` : dRefDak > 0.03 ? `${S(dRefDak)} από την αναφορά (${F(c.avg.dak)})` : `ίδιος με την αναφορά (${F(c.avg.dak)})`;
  let dakMsg = `<b>ΔΑΚ ${F(a.avg.dak)}</b>: ${vsPb} ${ev.relay ? 'τον καλύτερο αγώνα της ομάδας' : 'το ΑΡ'} (${F(b.avg.dak)}), ${vsRef}.`;
  if (bl.length >= 4 && ev.style !== 'im') {
    const h = Math.floor(bl.length / 2);
    const rise = avg(bl.slice(h), 'dak') - avg(bl.slice(0, h), 'dak');
    dakMsg += rise >= 0.3 ? ` Ανεβαίνει ${S(rise)} στο 2ο μισό.` : ' Σταθερός σε όλο τον αγώνα.';
  }
  out.push(dakMsg);
  const top = R.recs.find(r => r.gain !== null);
  if (top) out.push(`<b>Η μεγαλύτερη ευκαιρία: ${top.title.toLowerCase()}</b>, περίπου ${S(-top.gain)} s. Με τις ${Math.min(4, R.recs.filter(r => r.gain !== null).length)} πρώτες προτάσεις ο στόχος είναι <b>${T(R.targetTime)}</b>.`);
  else out.push('Σε όλες τις φάσεις ο αγώνας είναι κοντά στην αναφορά.');
  return out;
}

module.exports = { recommendations, messages, legPbs, T, F, S, avg, sum, U, FLY_BENEFIT, IN_STYLE };
