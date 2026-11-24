// Fictional races for the report templates (50 m pool). Every number of a race comes from one simulation, so that the times,
// the speeds, the strokes and the turns agree with each other the way the analysis computes them (SwimCalculator / swimAnalyze_).
'use strict';

const IM_ORDER = ['fly', 'bk', 'br', 'fs'];
const STYLE_NAME = { fs: 'Ελεύθερο', bk: 'Ύπτιο', br: 'Πρόσθιο', fly: 'Πεταλούδα', im: 'Μικτή' };
const PPC = s => (s === 'fs' || s === 'bk') ? 2 : 1;

// Seeded noise, the same numbers every time.
function rng(seed) {
  let s = seed >>> 0;
  return () => { s = (s * 1664525 + 1013904223) >>> 0; return s / 4294967296; };
}

function pattern(distance) {
  if (distance <= 200) return [15, 25, 35, 45, 50];
  if (distance <= 400) return [15, 25, 45, 50];
  return [15, 45, 50];
}

function checkpoints(distance) {
  const out = [];
  for (let wall = 0; wall < distance; wall += 50) pattern(distance).forEach(p => out.push(wall + p));
  return out;
}

const r2 = x => Math.round(x * 100) / 100;
const half = x => Math.round(x * 2) / 2;

/**
 * One race. p: { distance, style, target (final time), shape(L, i) relative clean speed of segment i of length L, sr(L, i) cycles/min,
 * start: { reaction, block, flight, entryDist, entryVel, kicks, bt, bd, boFactor }, turn(L): { in5, feetOff, lastGap, uw, kicks, bd,
 * boFactor, open }, finish: { last5, lastGap }, noise }.
 */
function simulate(p) {
  const D = p.distance, n = D / 50, pat = pattern(D);
  const styleAt = d => p.style === 'im' ? IM_ORDER[Math.min(3, Math.floor((d - 1e-6) / (D / 4)))] : p.style;
  const segs = []; // clean segments per length
  for (let L = 0; L < n; L++) {
    const inner = pat.filter(x => x >= 15 && x <= 45);
    const list = [];
    for (let i = 0; i + 1 < inner.length; i++) list.push([L * 50 + inner[i], L * 50 + inner[i + 1]]);
    segs.push(list);
  }
  const noise = rng(p.seed || 7);
  const jit = segs.map(list => list.map(() => 1 + (noise() - 0.5) * (p.noise || 0)));
  let scale = 1.7;
  let res = null;
  for (let iter = 0; iter < 60; iter++) {
    res = run(scale);
    const err = res.final - p.target;
    if (Math.abs(err) < 1e-6) break;
    // d(final)/d(scale) ≈ −clean/scale
    scale *= 1 + err / Math.max(1, res.cleanTime);
  }
  return res;

  function run(k) {
    const t = {}; // checkpoint times
    const v = (L, i) => k * p.shape(L, i) * jit[L][i];
    const s = p.start;
    const v1 = v(0, 0);
    const bo15Start = s.boFactor * v1;
    const t15 = s.bt + (15 - s.bd) / bo15Start;
    t[15] = t15;
    let cleanTime = 0;
    const turns = [];
    for (let L = 0; L < n; L++) {
      const w0 = L * 50;
      if (L > 0) {
        const tr = turns[L - 1];
        t[w0 + 15] = t[w0] + tr.out15;
      }
      segs[L].forEach(([a, b], i) => {
        const dt = (b - a) / v(L, i);
        t[b] = t[a] + dt;
        cleanTime += dt;
      });
      const wEnd = w0 + 50;
      if (L < n - 1) {
        const tp = p.turn(L);
        t[wEnd] = t[w0 + 45] + tp.in5;
        const vNext = v(L + 1, 0);
        const bo = tp.boFactor * vNext;
        const out15 = tp.feetOff + tp.uw + (15 - tp.bd) / bo;
        const styleIn = styleAt(wEnd), styleOut = styleAt(wEnd + 1);
        const open = tp.open !== undefined ? tp.open : !((styleIn === 'fs' || styleIn === 'bk') && styleOut === styleIn);
        turns.push({ wall: wEnd, styleIn, styleOut, open, in5: tp.in5, out15, inOut: tp.in5 + out15, feetOff: tp.feetOff, lastGap: tp.lastGap,
          turnTime: open ? tp.feetOff : tp.lastGap + tp.feetOff, uw: tp.uw, kicks: tp.kicks, bd: tp.bd, vUw: tp.bd / tp.uw,
          vBo15: bo, kind: turnKind(styleIn, styleOut, open) });
      } else {
        t[wEnd] = t[w0 + 45] + p.finish.last5;
      }
    }
    return { t, final: t[D], cleanTime, turns, scale: k, v, t15, bo15Start };
  }
}

function turnKind(a, b, open) {
  if (a !== b) return { 'fly-bk': 'Πεταλούδα → ύπτιο', 'bk-br': 'Ύπτιο → πρόσθιο', 'br-fs': 'Πρόσθιο → ελεύθερο' }[a + '-' + b] || 'Αλλαγή στυλ';
  if (!open) return 'Τούμπα';
  return 'Ανοιχτή (επαφή με τα χέρια)';
}

/** The analysis of a simulated race, as the report computes it. */
function analyse(p, sim) {
  const D = p.distance, cps = checkpoints(D), ppcOf = d => PPC(styleAt(p, d));
  const rows = [];
  const srOf = {};
  for (let L = 0; L < D / 50; L++) {
    const inner = pattern(D).filter(x => x >= 15 && x <= 45);
    for (let i = 0; i + 1 < inner.length; i++) srOf[L * 50 + inner[i + 1]] = p.sr(L, i);
  }
  cps.forEach((d, idx) => {
    const prevD = idx === 0 ? 0 : cps[idx - 1];
    const tPrev = idx === 0 ? 0 : sim.t[prevD];
    const seg = sim.t[d] - tPrev;
    const lastWall = Math.floor((prevD + 1e-9) / 50) * 50;
    const isSwim = prevD >= lastWall + 15;
    const isFree = isSwim && d <= lastWall + 45;
    const style = styleAt(p, d);
    const ppc = PPC(style);
    const row = { d, prevD, t: sim.t[d], seg, vel: (d - prevD) / seg, isFree, style, ppc, strokes: null, sr: null, sl: null, dps: null, tempo: null };
    if (isFree) {
      const sr = srOf[d];
      const cycles = seg * sr / 60;
      row.strokes = half(cycles * ppc);
      row.sr = sr;
      row.sl = row.vel * 60 / sr;
      row.dps = row.sl / ppc;
      row.tempo = 60 / sr;
      row.si = row.vel * row.sl;
    }
    rows.push(row);
  });
  // Strokes of the other segments (for the 50s, the ΔΑΚ and the Swolf): from the rate of the nearest clean segment.
  rows.forEach((r, i) => {
    if (r.strokes !== null) return;
    const near = rows.slice(Math.max(0, i - 3), i + 4).filter(x => x.sr !== null);
    const sr = near.length ? near[near.length - 1].sr : 40;
    let swimTime;
    const L = Math.floor((r.prevD + 1e-9) / 50);
    if (r.prevD === 0) swimTime = sim.t[15] - p.start.bt;
    else if (r.d % 50 === 0 && r.d === D) swimTime = r.seg - p.finish.lastGap;
    else if (r.d % 50 === 0) swimTime = r.seg - p.turn(L).lastGap;
    else { const tr = sim.turns[L - 1]; swimTime = r.seg - tr.feetOff - tr.uw; }
    r.strokes = half(Math.max(0, swimTime) * sr / 60 * r.ppc);
  });
  const byD = {};
  rows.forEach(r => { byD[r.d] = r; });
  // The 50s.
  const blocks = [];
  for (let b = 0; b < D; b += 50) {
    const e = b + 50;
    const inB = rows.filter(r => r.prevD >= b - 1e-9 && r.d <= e + 1e-9);
    const time = sim.t[e] - (b === 0 ? 0 : sim.t[b]);
    const strokes = inB.reduce((s, r) => s + r.strokes, 0);
    const win = rows.filter(r => r.prevD >= b + 15 - 1e-9 && r.d <= e + 1e-9);
    const dak = (win.reduce((s, r) => s + r.strokes, 0) + (sim.t[e] - sim.t[b + 15])) / 3.5;
    let cyc = 0, ct = 0, cd = 0, cs = 0;
    inB.filter(r => r.sr !== null).forEach(r => { const c = r.sr * r.seg / 60; cyc += c; ct += r.seg; cd += r.d - r.prevD; cs += c * r.ppc; });
    const sr = ct > 0 ? cyc * 60 / ct : null;
    const sl = cyc > 0 ? cd / cyc : null;
    const vClean = ct > 0 ? cd / ct : null;
    blocks.push({ from: b, to: e, time, cum: sim.t[e], strokes, swolf: time + strokes, dak, sr, sl, dps: cs > 0 ? cd / cs : null, vel: 50 / time,
      vClean, tempo: sr ? 60 / sr : null, si: vClean && sl ? vClean * sl : null, style: styleAt(p, e),
      first25: rows.find(r => r.isFree && r.prevD === b + 15) || null, second25: rows.find(r => r.isFree && r.d === b + 45 && r.prevD === b + 25) || null });
  }
  // The start.
  const s = p.start;
  const entryT = s.block + s.flight;
  const start = {
    reaction: s.reaction, block: s.block, flight: s.flight, entryT, entryDist: s.entryDist, entryVel: s.entryVel, sigEntryVel: s.entryDist / entryT,
    kicks: s.kicks, bt: s.bt, uwDist: s.bd - s.entryDist, bd: s.bd, entryBoVel: (s.bd - s.entryDist) / (s.bt - entryT), sigBoVel: s.bd / s.bt,
    t15: sim.t[15], bo15Vel: sim.bo15Start
  };
  const lastX = [15, 25, 35].find(k => byD[D - k]) || 5;
  const finish = { last5: p.finish.last5, lastGap: p.finish.lastGap, lastX, lastXt: sim.t[D] - sim.t[D - lastX] };
  const lastSeg = rows.filter(r => r.isFree).slice(-1)[0];
  finish.vLast5 = 5 / p.finish.last5;
  finish.vBefore = lastSeg ? lastSeg.vel : null;
  // The phases (start, swims, turns, finish), as the short report (swimV2Phases_).
  const phases = [];
  const span = (a, b) => (b === 0 ? 0 : sim.t[b]) - (a === 0 ? 0 : sim.t[a]);
  phases.push({ kind: 'start', from: 0, to: 15, t: span(0, 15) });
  let prev = 15;
  for (let w = 50; w < D; w += 50) {
    phases.push({ kind: 'swim', from: prev, to: w - 5, t: span(prev, w - 5) });
    phases.push({ kind: 'turn', from: w - 5, to: w + 15, wall: w, t: span(w - 5, w + 15) });
    prev = w + 15;
  }
  phases.push({ kind: 'swim', from: prev, to: D - 5, t: span(prev, D - 5) });
  phases.push({ kind: 'finish', from: D - 5, to: D, t: span(D - 5, D) });
  const sum = k => phases.filter(x => x.kind === k).reduce((a, x) => a + x.t, 0);
  const composition = { start: sum('start'), swim: sum('swim'), turn: sum('turn'), finish: sum('finish') };
  // Clean swimming averages (weighted).
  let cyc = 0, ct = 0, cd = 0;
  rows.filter(r => r.sr !== null).forEach(r => { cyc += r.sr * r.seg / 60; ct += r.seg; cd += r.d - r.prevD; });
  const avg = { sr: cyc * 60 / ct, sl: cd / cyc, vClean: cd / ct };
  avg.tempo = 60 / avg.sr;
  avg.si = avg.vClean * avg.sl;
  avg.dak = blocks.reduce((a, b) => a + b.dak, 0) / blocks.length;
  avg.turnInOut = sim.turns.length ? sim.turns.reduce((a, x) => a + x.inOut, 0) / sim.turns.length : null;
  return { p, sim, rows, byD, blocks, start, finish, phases, composition, avg, final: sim.t[D], turns: sim.turns, cps };
}

function styleAt(p, d) {
  return p.style === 'im' ? IM_ORDER[Math.min(3, Math.floor((d - 1e-6) / (p.distance / 4)))] : p.style;
}

// ---------------------------------------------------------------------------------------------------------------------------------
// The races. Men, 50 m pool. The PB is an earlier race of the same (fictional) athlete; the reference a fictional final.

function freestyle(distance, o) {
  const n = distance / 50;
  return {
    distance, style: 'fs', target: o.target, seed: o.seed, noise: o.noise || 0,
    shape: o.shape, sr: o.sr,
    start: o.start,
    turn: L => { const t = Object.assign({ feetOff: 0.24, lastGap: 0.64 }, o.turn(L, n)); if (t.uw === undefined) t.uw = t.bd / t.push; return t; },
    finish: o.finish
  };
}

function variants(make, base, pb, ref) {
  return { race: analyse(make(base), simulate(make(base))), pb: analyse(make(pb), simulate(make(pb))), ref: analyse(make(ref), simulate(make(ref))) };
}

const R = {};

// ---- 50 ελεύθερο
R['50'] = (() => {
  const mk = o => freestyle(50, {
    target: o.target, shape: (L, i) => [1.02, 1.0, 0.976][i] * (o.fade ? [1, 1, 1 - o.fade][i] : 1), sr: (L, i) => [59.0, 58.2, 57.0][i] + (o.srAdd || 0),
    start: Object.assign({ reaction: 0.40, block: 0.65, flight: 0.31, entryDist: 3.60, entryVel: 4.45, kicks: 6, bt: 4.35, bd: 11.6, boFactor: 1.03 }, o.start || {}),
    turn: () => ({}), finish: Object.assign({ last5: 2.36, lastGap: 0.42 }, o.finish || {})
  });
  return variants(mk,
    { target: 22.68 },
    { target: 22.79, srAdd: -0.9, start: { reaction: 0.41, block: 0.63, entryDist: 3.55, entryVel: 4.38, bt: 4.30, bd: 11.4 }, finish: { last5: 2.42, lastGap: 0.55 }, fade: 0.012 },
    { target: 21.95, start: { reaction: 0.38, block: 0.62, flight: 0.30, entryDist: 3.85, entryVel: 4.75, kicks: 7, bt: 4.45, bd: 13.4, boFactor: 1.03 },
      finish: { last5: 2.24, lastGap: 0.38 }, srAdd: 1.5 });
})();

// ---- 100 ελεύθερο
R['100'] = (() => {
  const mk = o => freestyle(100, {
    target: o.target,
    shape: (L, i) => (L === 0 ? [1.03, 1.015, 1.0] : [0.975 + (o.hold || 0), 0.955 + (o.hold || 0), 0.935 + (o.hold || 0)])[i],
    sr: (L, i) => (L === 0 ? [52.5, 52.0, 51.4] : [51.0, 50.2, 49.0])[i] + (o.srAdd || 0),
    start: Object.assign({ reaction: 0.41, block: 0.67, flight: 0.31, entryDist: 3.55, entryVel: 4.35, kicks: 5, bt: 4.25, bd: 11.0, boFactor: 1.03 }, o.start || {}),
    turn: () => Object.assign({ in5: 2.55, push: 2.40, kicks: 5, bd: 9.8, boFactor: 1.03 }, o.turn || {}),
    finish: Object.assign({ last5: 2.62, lastGap: 0.48 }, o.finish || {})
  });
  return variants(mk,
    { target: 48.95 },
    { target: 49.31, srAdd: -0.6, start: { block: 0.68, flight: 0.32, entryDist: 3.48, entryVel: 4.28, kicks: 4, bt: 4.40, bd: 10.6 }, turn: { in5: 2.61, push: 2.30, kicks: 4, bd: 8.9 }, finish: { last5: 2.66, lastGap: 0.53 }, hold: 0.01 },
    { target: 47.95, start: { reaction: 0.39, block: 0.63, flight: 0.30, entryDist: 3.80, entryVel: 4.70, kicks: 6, bt: 4.55, bd: 12.6 },
      turn: { in5: 2.42, push: 2.50, kicks: 6, bd: 11.6 }, finish: { last5: 2.50, lastGap: 0.40 }, hold: 0.012, srAdd: 1.0 });
})();

// ---- 200 ελεύθερο
R['200'] = (() => {
  const mk = o => freestyle(200, {
    target: o.target, seed: 11, noise: 0.006,
    shape: (L, i) => [[1.035, 1.025, 1.015], [1.0, 0.995, 0.99], [0.99, 0.985, 0.98], [0.985 + (o.kick || 0), 0.985 + (o.kick || 0), 0.99 + (o.kick || 0)]][L][i],
    sr: (L, i) => [[44.8, 44.4, 44.0], [43.6, 43.4, 43.2], [43.4, 43.2, 43.0], [44.2, 44.8, 45.6]][L][i] + (o.srAdd || 0),
    start: Object.assign({ reaction: 0.42, block: 0.69, flight: 0.30, entryDist: 3.50, entryVel: 4.25, kicks: 5, bt: 4.30, bd: 10.8, boFactor: 1.03 }, o.start || {}),
    turn: L => Object.assign({ in5: [2.78, 2.82, 2.86][L], push: [2.34, 2.31, 2.28][L], kicks: [5, 5, 4][L], bd: [10.2, 9.8, 9.3][L], boFactor: 1.04,
      lastGap: [0.64, 0.66, 0.70][L] }, o.turn ? o.turn(L) : {}),
    finish: Object.assign({ last5: 2.84, lastGap: 0.50 }, o.finish || {})
  });
  return variants(mk,
    { target: 109.20 },
    { target: 109.95, srAdd: 0.5, start: { block: 0.70, flight: 0.31, entryDist: 3.45, entryVel: 4.18, kicks: 4, bt: 4.10, bd: 10.2 }, finish: { last5: 2.90, lastGap: 0.56 }, turn: L => ({ in5: [2.80, 2.86, 2.92][L], push: [2.26, 2.22, 2.18][L], kicks: [4, 4, 4][L], bd: [9.4, 9.0, 8.6][L] }), kick: -0.01 },
    { target: 106.10, start: { reaction: 0.40, block: 0.65, flight: 0.30, entryDist: 3.70, entryVel: 4.55, kicks: 6, bt: 4.50, bd: 12.2 },
      turn: L => ({ in5: [2.62, 2.66, 2.70][L], push: [2.44, 2.42, 2.40][L], kicks: [6, 6, 6][L], bd: [11.6, 11.3, 11.0][L] }), finish: { last5: 2.70, lastGap: 0.42 }, srAdd: 0.6 });
})();

// ---- 400 ελεύθερο (περάσματα 15 · 25 · 45 · 50)
R['400'] = (() => {
  const lenF = [1.045, 1.0, 0.99, 0.986, 0.984, 0.985, 0.99, 1.02];
  const srF = [1.03, 1.0, 0.99, 0.985, 0.985, 0.99, 1.0, 1.05];
  const mk = o => freestyle(400, {
    target: o.target, seed: 23, noise: 0.006,
    shape: (L, i) => (o.lenF || lenF)[L] * [1.03, 1.0][i],
    sr: (L, i) => 41.5 * srF[L] + [0.8, 0][i] + (o.srAdd || 0),
    start: Object.assign({ reaction: 0.43, block: 0.71, flight: 0.30, entryDist: 3.45, entryVel: 4.15, kicks: 4, bt: 4.15, bd: 10.0, boFactor: 1.03 }, o.start || {}),
    turn: L => Object.assign({ in5: 2.86 + 0.012 * L, push: 2.26 - 0.01 * L, kicks: L < 4 ? 4 : 3, bd: 9.4 - 0.12 * L, boFactor: 1.04, lastGap: 0.66 + 0.01 * L },
      o.turn ? o.turn(L) : {}),
    finish: Object.assign({ last5: 2.92, lastGap: 0.52 }, o.finish || {})
  });
  return variants(mk,
    { target: 232.60 },
    { target: 233.80, srAdd: -0.4, start: { block: 0.73, flight: 0.31, entryDist: 3.40, entryVel: 4.08, bt: 4.00, bd: 9.5 }, finish: { last5: 2.97, lastGap: 0.58 }, lenF: [1.05, 1.0, 0.99, 0.984, 0.98, 0.98, 0.985, 1.01], turn: L => ({ in5: 2.90 + 0.015 * L, push: 2.20 - 0.012 * L, bd: 8.9 - 0.15 * L }) },
    { target: 225.80, start: { reaction: 0.41, block: 0.67, flight: 0.30, entryDist: 3.65, entryVel: 4.45, kicks: 5, bt: 4.35, bd: 11.6 },
      turn: L => ({ in5: 2.70 + 0.006 * L, push: 2.36 - 0.005 * L, kicks: 5, bd: 11.0 - 0.06 * L }), finish: { last5: 2.78, lastGap: 0.45 }, srAdd: 0.8 });
})();

// ---- 400 μικτή (περάσματα 15 · 25 · 45 · 50)
R['400im'] = (() => {
  const styleF = { fly: 1.0, bk: 0.945, br: 0.835, fs: 1.0 };
  const srBase = { fly: 49.5, bk: 33.5, br: 37.0, fs: 42.0 };
  const mk = o => ({
    distance: 400, style: 'im', target: o.target, seed: 31, noise: 0.006,
    shape: (L, i) => {
      const st = IM_ORDER[Math.floor(L / 2)], second = L % 2 === 1;
      const f = (o.styleF || styleF)[st] * (L === 0 ? 1.04 : 1) * (second ? (st === 'fs' ? 1.01 : 0.975) : 1);
      return f * [1.025, 1.0][i];
    },
    sr: (L, i) => { const st = IM_ORDER[Math.floor(L / 2)]; return srBase[st] * (L % 2 === 1 ? (st === 'fs' ? 1.03 : 0.985) : 1) + [0.8, 0][i] + (o.srAdd || 0); },
    start: Object.assign({ reaction: 0.44, block: 0.72, flight: 0.31, entryDist: 3.40, entryVel: 4.10, kicks: 5, bt: 4.35, bd: 10.2, boFactor: 1.03 }, o.start || {}),
    turn: L => {
      const wall = (L + 1) * 50;
      const t = {
        50: { in5: 3.05, feetOff: 0.92, lastGap: 0.30, uw: 3.45, kicks: 5, bd: 8.6, boFactor: 1.04 },
        100: { in5: 3.10, feetOff: 1.05, lastGap: 0.32, uw: 4.05, kicks: 7, bd: 9.4, boFactor: 1.05 },
        150: { in5: 3.25, feetOff: 0.26, lastGap: 0.70, uw: 4.10, kicks: 7, bd: 9.2, boFactor: 1.05 },
        200: { in5: 3.40, feetOff: 1.10, lastGap: 0.70, uw: 4.45, kicks: 1, bd: 8.2, boFactor: 1.10 },
        250: { in5: 3.70, feetOff: 0.98, lastGap: 0.45, uw: 4.40, kicks: 1, bd: 8.0, boFactor: 1.10 },
        300: { in5: 3.65, feetOff: 1.00, lastGap: 0.45, uw: 3.50, kicks: 4, bd: 7.8, boFactor: 1.04 },
        350: { in5: 2.95, feetOff: 0.25, lastGap: 0.68, uw: 3.45, kicks: 4, bd: 7.6, boFactor: 1.04 }
      }[wall];
      return Object.assign({}, t, o.turn ? o.turn(wall, t) : {});
    },
    finish: Object.assign({ last5: 2.95, lastGap: 0.52 }, o.finish || {})
  });
  return variants(mk,
    { target: 261.50 },
    { target: 262.90, srAdd: -0.5, start: { block: 0.74, flight: 0.31, entryDist: 3.35, entryVel: 4.02, kicks: 4, bt: 4.15, bd: 9.6 }, finish: { last5: 3.02, lastGap: 0.58 }, styleF: { fly: 1.0, bk: 0.95, br: 0.825, fs: 0.995 }, turn: (w, t) => (w === 200 || w === 250 ? { uw: t.uw - 0.35, bd: t.bd - 0.6, in5: t.in5 + 0.08 } : {}) },
    { target: 253.80, start: { reaction: 0.41, block: 0.67, flight: 0.30, entryDist: 3.60, entryVel: 4.40, kicks: 6, bt: 4.50, bd: 11.4 },
      turn: (w, t) => ({ in5: t.in5 - 0.15, uw: t.uw + 0.25, bd: t.bd + 1.0, kicks: t.kicks + (w === 200 || w === 250 ? 0 : 1) }), finish: { last5: 2.80, lastGap: 0.46 }, srAdd: 0.6 });
})();

// ---- 800 ελεύθερο (περάσματα 15 · 45 · 50)
R['800'] = (() => {
  const n = 16;
  const lenF = L => L === 0 ? 1.05 : L === 1 ? 1.008 : L >= n - 1 ? 1.045 : L >= n - 2 ? 1.012 : 1.0 - 0.0012 * Math.min(L, 9) + 0.0008 * Math.max(0, L - 9);
  const srF = L => L === 0 ? 1.03 : L >= n - 1 ? 1.07 : L >= n - 2 ? 1.025 : 1.0 - 0.002 * Math.min(L, 9);
  const mk = o => freestyle(800, {
    target: o.target, seed: 41, noise: 0.007,
    shape: (L) => (o.lenF || lenF)(L),
    sr: (L) => 40.0 * srF(L) + (o.srAdd || 0),
    start: Object.assign({ reaction: 0.45, block: 0.73, flight: 0.29, entryDist: 3.40, entryVel: 4.00, kicks: 3, bt: 3.95, bd: 9.2, boFactor: 1.03 }, o.start || {}),
    turn: L => Object.assign({ in5: 2.92 + 0.006 * L, push: 2.20 - 0.005 * L, kicks: L < 6 ? 3 : 2, bd: 8.4 - 0.07 * L, boFactor: 1.04, lastGap: 0.68 + 0.004 * L },
      o.turn ? o.turn(L) : {}),
    finish: Object.assign({ last5: 2.98, lastGap: 0.55 }, o.finish || {})
  });
  return variants(mk,
    { target: 484.30 },
    { target: 486.10, srAdd: -0.6, start: { block: 0.75, flight: 0.30, entryDist: 3.35, entryVel: 3.95, bt: 3.85, bd: 8.8 }, finish: { last5: 3.05, lastGap: 0.60 }, lenF: L => L === 0 ? 1.055 : L === 1 ? 1.01 : L >= n - 1 ? 1.03 : 1.0 - 0.0018 * L, turn: L => ({ push: 2.14 - 0.006 * L, bd: 7.9 - 0.08 * L }) },
    { target: 471.50, start: { reaction: 0.42, block: 0.69, flight: 0.30, entryDist: 3.60, entryVel: 4.30, kicks: 4, bt: 4.20, bd: 10.6 },
      lenF: L => L === 0 ? 1.045 : L === 1 ? 1.006 : L >= n - 1 ? 1.05 : L >= n - 2 ? 1.02 : 1.0,
      turn: L => ({ in5: 2.80 + 0.003 * L, push: 2.30 - 0.003 * L, kicks: 4, bd: 10.0 - 0.03 * L }), finish: { last5: 2.84, lastGap: 0.48 }, srAdd: 0.8 });
})();

// ---- 1500 ελεύθερο (περάσματα 15 · 45 · 50)
R['1500'] = (() => {
  const n = 30;
  const lenF = L => L === 0 ? 1.05 : L === 1 ? 1.01 : L >= n - 1 ? 1.05 : L >= n - 2 ? 1.018 : L >= n - 4 ? 1.004 : 1.0 - 0.0009 * Math.min(L, 18) + 0.0004 * Math.max(0, L - 18);
  const srF = L => L === 0 ? 1.03 : L >= n - 1 ? 1.08 : L >= n - 2 ? 1.035 : L >= n - 4 ? 1.012 : 1.0 - 0.0015 * Math.min(L, 18);
  const mk = o => freestyle(1500, {
    target: o.target, seed: 53, noise: 0.008,
    shape: (L) => (o.lenF || lenF)(L),
    sr: (L) => 39.0 * srF(L) + (o.srAdd || 0),
    start: Object.assign({ reaction: 0.46, block: 0.74, flight: 0.29, entryDist: 3.35, entryVel: 3.95, kicks: 3, bt: 3.90, bd: 9.0, boFactor: 1.03 }, o.start || {}),
    turn: L => Object.assign({ in5: 2.95 + 0.004 * L, push: 2.18 - 0.003 * L, kicks: L < 10 ? 3 : 2, bd: 8.2 - 0.045 * L, boFactor: 1.04, lastGap: 0.70 + 0.003 * L },
      o.turn ? o.turn(L) : {}),
    finish: Object.assign({ last5: 3.02, lastGap: 0.56 }, o.finish || {})
  });
  return variants(mk,
    { target: 922.80 },
    { target: 926.40, srAdd: -0.5, start: { block: 0.76, flight: 0.30, entryDist: 3.30, entryVel: 3.90, kicks: 2, bt: 3.70, bd: 8.4 }, finish: { last5: 3.10, lastGap: 0.62 }, lenF: L => L === 0 ? 1.05 : L === 1 ? 1.01 : L >= n - 1 ? 1.035 : 1.0 - 0.0012 * L, turn: L => ({ push: 2.12 - 0.004 * L, bd: 7.7 - 0.05 * L }) },
    { target: 902.40, start: { reaction: 0.43, block: 0.70, flight: 0.30, entryDist: 3.55, entryVel: 4.25, kicks: 4, bt: 4.15, bd: 10.4 },
      lenF: L => L === 0 ? 1.045 : L === 1 ? 1.006 : L >= n - 1 ? 1.06 : L >= n - 2 ? 1.025 : 1.0,
      turn: L => ({ in5: 2.82 + 0.002 * L, push: 2.28 - 0.002 * L, kicks: 4, bd: 9.8 - 0.02 * L }), finish: { last5: 2.86, lastGap: 0.48 }, srAdd: 0.8 });
})();

module.exports = { R, STYLE_NAME, IM_ORDER, PPC, checkpoints, pattern, styleAt };

if (require.main === module) {
  const T = x => x < 60 ? x.toFixed(2) : Math.floor(x / 60) + ':' + (x % 60 < 10 ? '0' : '') + (x % 60).toFixed(2);
  Object.keys(R).forEach(k => {
    const { race, pb, ref } = R[k];
    const b = race.blocks.map(x => x.time.toFixed(2)).join(' ');
    console.log(k, 'final', T(race.final), 'pb', T(pb.final), 'ref', T(ref.final), '| t15', race.start.t15.toFixed(2), '| 50s', b.slice(0, 140));
    console.log('   clean v', race.avg.vClean.toFixed(3), 'sr', race.avg.sr.toFixed(1), 'sl', race.avg.sl.toFixed(2), 'tempo', race.avg.tempo.toFixed(2), 'dak', race.avg.dak.toFixed(2),
      '| comp', Object.entries(race.composition).map(([a, v]) => a + ' ' + v.toFixed(2)).join(' '),
      '| turns', race.turns.slice(0, 3).map(t => t.in5.toFixed(2) + '/' + t.out15.toFixed(2)).join(' '));
  });
}
