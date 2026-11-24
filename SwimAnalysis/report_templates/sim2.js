// Fictional races for the report templates, for every event: 50 m, 25 m and 25 yd pools, all styles, medley, relays.
// One simulation per race, so that the times, the speeds, the strokes, the turns and the ΔΑΚ agree with each other the way
// the analysis computes them (SwimCalculator / swimAnalyze_). Distances in pool units (m or yd); speeds shown in m/s.
'use strict';

const IM_ORDER = ['fly', 'bk', 'br', 'fs'];
const MEDLEY_RELAY = ['bk', 'br', 'fly', 'fs'];
const STYLE_NAME = { fs: 'Ελεύθερο', bk: 'Ύπτιο', br: 'Πρόσθιο', fly: 'Πεταλούδα', im: 'Μικτή' };
const PPC = s => (s === 'fs' || s === 'bk') ? 2 : 1;
const YD = 0.9144;

function rng(seed) {
  let s = (seed * 2654435761) >>> 0 || 1;
  return () => { s = (s * 1664525 + 1013904223) >>> 0; return s / 4294967296; };
}
const half = x => Math.round(x * 2) / 2;

/** The checkpoints of the program (SwimPresets.DefaultCheckpoints). */
function presets(distance, pool, relay) {
  const leg = relay ? distance / 4 : distance;
  let pattern;
  if (pool === 25) pattern = [15, 20, 25];
  else if (leg <= 50 && relay) pattern = [15, 25, 35, 45, 50];
  else if (relay && leg <= 100) pattern = [15, 25, 45, 50];
  else if (relay) pattern = [15, 45, 50];
  else if (distance <= 200) pattern = [15, 25, 35, 45, 50];
  else if (distance <= 400) pattern = [15, 25, 45, 50];
  else pattern = [15, 45, 50];
  const out = [];
  const length = pool === 25 ? 25 : 50;
  for (let wall = 0; wall < distance; wall += length) pattern.forEach(p => {
    const d = wall + p;
    if (d <= distance && !out.includes(d)) out.push(d);
  });
  return { list: out.sort((a, b) => a - b), pattern };
}

// Style parameters (men, metres). sr: cycles/min by distance; clean speed relative to freestyle.
const STY = {
  fs: { speed: 1.0, sr: [[50, 58.5], [100, 51.5], [200, 44.5], [400, 41.5], [800, 40], [1500, 39]],
    start: { reaction: 0.41, block: 0.66, flight: 0.31, entryDist: 3.55, entryVel: 4.35, kicks: 6, bd: 11.4, aUw: 1.13, boFactor: 1.03 },
    turn: { aIn: 1.06, feetOff: 0.24, lastGap: 0.64, aPush: 1.25, bd: 10.2, kicks: 5, boFactor: 1.04 },
    finish: { aFin: 1.03, lastGap: 0.46 } },
  bk: { speed: 0.895, sr: [[50, 52], [100, 46], [200, 40], [400, 37]],
    start: { reaction: 0.36, block: 0.58, flight: 0.36, entryDist: 2.95, entryVel: 3.65, kicks: 9, bd: 12.8, aUw: 1.16, boFactor: 1.04 },
    turn: { aIn: 1.02, feetOff: 0.27, lastGap: 0.72, aPush: 1.36, bd: 11.2, kicks: 7, boFactor: 1.04 },
    finish: { aFin: 0.99, lastGap: 0.38 } },
  br: { speed: 0.79, sr: [[50, 59], [100, 50], [200, 38.5], [400, 37]],
    start: { reaction: 0.42, block: 0.67, flight: 0.31, entryDist: 3.45, entryVel: 4.10, kicks: 1, bd: 11.0, aUw: 1.05, boFactor: 1.12 },
    turn: { aIn: 1.07, feetOff: 0.94, lastGap: 0.42, aPush: 1.26, bd: 8.6, kicks: 1, boFactor: 1.12 },
    finish: { aFin: 1.08, lastGap: 0.40 } },
  fly: { speed: 0.935, sr: [[50, 58.5], [100, 52], [200, 46], [400, 44]],
    start: { reaction: 0.41, block: 0.66, flight: 0.31, entryDist: 3.60, entryVel: 4.30, kicks: 7, bd: 11.8, aUw: 1.18, boFactor: 1.05 },
    turn: { aIn: 1.0, feetOff: 0.88, lastGap: 0.30, aPush: 1.30, bd: 9.4, kicks: 5, boFactor: 1.05 },
    finish: { aFin: 1.0, lastGap: 0.28 } }
};
// Stroke rate of each style inside the medley (cycles/min) by the medley distance.
// Speed of each style inside the medley (on the speed of the style): the butterfly fresh, the freestyle at the end of it.
const IM_F = { 100: { fly: 1.06, bk: 1.0, br: 1.0, fs: 0.95 }, 200: { fly: 1.07, bk: 1.0, br: 1.0, fs: 0.93 }, 400: { fly: 1.06, bk: 1.0, br: 1.0, fs: 0.95 } };
const IM_SR = { 100: { fly: 56, bk: 48, br: 54, fs: 54 }, 200: { fly: 52, bk: 43, br: 45, fs: 48 }, 400: { fly: 49.5, bk: 37, br: 37.5, fs: 42 } };

function srBase(style, dist) {
  const t = STY[style].sr;
  if (dist <= t[0][0]) return t[0][1];
  for (let i = 1; i < t.length; i++) if (dist <= t[i][0]) {
    const [d0, s0] = t[i - 1], [d1, s1] = t[i];
    const f = Math.log(dist / d0) / Math.log(d1 / d0);
    return s0 + (s1 - s0) * f;
  }
  return t[t.length - 1][1];
}

/** Relative clean speed of the 50 j of n 50s of a unit (race or relay leg) of distance dist. */
function pace50(j, n, dist, kick) {
  if (n === 1) return 1.0;
  if (dist <= 100) return [1.0, 0.955][Math.min(j, 1)];
  if (dist <= 200) return [1.035, 1.0, 0.99, 0.985 + (kick || 0)][Math.min(j, 3)];
  if (dist <= 500) {
    const base = j === 0 ? 1.045 : 1.0 - 0.003 * Math.min(j, n - 3);
    return j === n - 1 ? 1.02 : j === n - 2 ? Math.max(base, 0.988) : base;
  }
  if (j === 0) return 1.05;
  if (j === 1) return 1.008;
  if (j === n - 1) return 1.045;
  if (j === n - 2) return 1.012;
  return 1.0 - 0.0011 * Math.min(j, Math.round(n * 0.6)) + 0.0005 * Math.max(0, j - Math.round(n * 0.6));
}

/**
 * One race. ev: { pool, unit, style, distance, relay }. v: variant parameters { target, seed, srAdd, start {}, turn {}, finish {},
 * exchanges [], legs [] (leg speed factors), kick }.
 */
function simulate(ev, v) {
  const D = ev.distance, P = ev.pool, n = D / P, k = ev.unit === 'yd' ? YD : 1;
  const legLen = ev.relay ? D / 4 : D;
  const pre = presets(D, P, ev.relay);
  const styleOfLength = L => {
    const d0 = L * P;
    if (ev.relay && ev.style === 'im') return MEDLEY_RELAY[Math.floor(d0 / legLen)];
    if (ev.style === 'im') return IM_ORDER[Math.floor(d0 / (D / 4))];
    return ev.style;
  };
  const legOfLength = L => ev.relay ? Math.floor(L * P / legLen) : 0;
  const inner = pre.pattern.filter(p => p >= 15 && p <= P - 5);
  const segsOf = L => { const out = []; for (let i = 0; i + 1 < inner.length; i++) out.push([L * P + inner[i], L * P + inner[i + 1]]); return out; };
  const noise = rng(v.seed || 7);
  const jit = Array.from({ length: n }, () => Array.from({ length: Math.max(1, inner.length - 1) }, () => 1 + (noise() - 0.5) * (v.noise === undefined ? 0.008 : v.noise)));
  // The unit of the pacing: the relay leg, the style of the medley (a quarter), or the race.
  const unitOf = L => {
    if (ev.relay) return { len: legLen, at: (L * P) % legLen };
    if (ev.style === 'im') return { len: D / 4, at: (L * P) % (D / 4), im: true };
    return { len: D, at: L * P };
  };
  const lengthFactor = L => {
    const u = unitOf(L);
    const nb = Math.max(1, u.len / 50), j = Math.floor(u.at / 50);
    let f = u.im ? (j === 0 ? 1.0 : 0.975) : pace50(j, nb, u.len, v.kick);
    if (P === 25 && (u.at % 50) >= 25) f *= 0.985;
    if (u.im && styleOfLength(L) === 'fs' && j === nb - 1 && nb > 1) f *= 1.03;
    return f;
  };
  const segProfile = (i, m) => m === 3 ? [1.02, 1.0, 0.98][i] : m === 2 ? [1.03, 1.0][i] : 1.0;
  const legF = L => ev.relay ? (v.legs || [1, 1, 1, 1])[legOfLength(L)] : 1;
  const imF = L => ev.style === 'im' && !ev.relay ? IM_F[D <= 100 ? 100 : D <= 200 ? 200 : 400][styleOfLength(L)] * ((v.styleF || {})[styleOfLength(L)] || 1) : 1;
  const dc = legLen >= 400 ? 2 : legLen >= 200 ? 1 : 0;   // distance class: shorter underwater, fewer kicks
  // The final time falls as the speed rises: geometric bisection on the speed (pool units / s).
  let lo = 0.2, hi = 6, res = null;
  for (let iter = 0; iter < 90; iter++) {
    const mid = Math.sqrt(lo * hi);
    res = run(mid);
    if (res.final > v.target) lo = mid; else hi = mid;
  }
  res = run(Math.sqrt(lo * hi));
  return res;

  function speed(L, i, s) {
    const m = segsOf(L).length;
    return s * STY[styleOfLength(L)].speed * imF(L) * legF(L) * lengthFactor(L) * segProfile(i, m) * jit[L][Math.min(i, jit[L].length - 1)];
  }

  function run(s) {
    const t = {}, turns = [];
    const st0 = styleOfLength(0);
    const sp = Object.assign({}, STY[st0].start, v.start || {});
    sp.kicks = Math.max(st0 === 'br' ? 1 : 2, sp.kicks - dc);
    sp.bd = Math.min(14.0, (sp.bd - 0.7 * dc) / k);
    sp.entryDist = sp.entryDist / k;
    const entryT = sp.block + sp.flight;
    const v0 = segsOf(0).length ? speed(0, 0, s) : s;
    const bt = entryT + (sp.bd - sp.entryDist) / (sp.aUw * v0);
    const bo15Start = sp.boFactor * v0;
    t[15] = bt + (15 - sp.bd) / bo15Start;
    let cleanTime = 0;
    for (let L = 0; L < n; L++) {
      const w0 = L * P;
      if (L > 0) t[w0 + 15] = t[w0] + turns[L - 1].out15;
      const segs = segsOf(L);
      segs.forEach(([a, b], i) => { const dt = (b - a) / speed(L, i, s); t[b] = t[a] + dt; cleanTime += dt; });
      const last = segs.length ? segs[segs.length - 1][1] : w0 + 15;
      const wEnd = w0 + P;
      const styleIn = styleOfLength(L);
      if (L < n - 1) {
        const styleOut = styleOfLength(L + 1);
        const exchange = ev.relay && wEnd % legLen === 0;
        const vNext = segsOf(L + 1).length ? speed(L + 1, 0, s) : s;
        let tp, kind;
        if (exchange) {
          const legIdx = wEnd / legLen; // the leg that starts
          const ex = (v.exchanges || [0.2, 0.2, 0.2])[legIdx - 1];
          const fin = Object.assign({}, STY[styleIn].finish, v.finish || {});
          const vIn = segs.length ? speed(L, segs.length - 1, s) : s;
          const bd = (10.8 - 0.5 * dc + (v.exBd || 0)) / k, entry = 3.45 / k, uwEntry = (bd - entry) / (1.15 * vNext);
          const bo = 1.04 * vNext;
          tp = { in5: 5 / (vIn * (fin.aFin + (v.finAdd || 0))), feetOff: ex, lastGap: fin.lastGap, uw: 0.30 + uwEntry, kicks: Math.max(2, 5 - dc + (v.exKicks || 0)), bd, boFactor: 1.04, ex,
            out15: ex + 0.30 + uwEntry + (15 - bd) / bo, vBo15: bo };
          kind = 'exchange';
        } else {
          const open = !((styleIn === 'fs' || styleIn === 'bk') && styleOut === styleIn);
          const base = Object.assign({}, STY[styleOut].turn);
          const vIn = segs.length ? speed(L, segs.length - 1, s) : s;
          let aIn = STY[styleIn].turn.aIn;
          if (styleIn !== styleOut) {
            // A change of style in the medley: touch, turn, push in the next style.
            aIn = STY[styleIn].turn.aIn - (styleIn === 'bk' ? 0.05 : 0.01);
            base.feetOff = { 'fly-bk': 1.02, 'bk-br': 1.10, 'br-fs': 0.95, 'bk-fly': 0.95 }[styleIn + '-' + styleOut] || 1.0;
            base.lastGap = STY[styleIn].turn.lastGap;
          }
          const tv = v.turn || {};
          const idx = turns.filter(x => x.kind !== 'exchange').length;
          const pr = Object.assign({}, base);
          pr.in5 = 5 / (vIn * (aIn + (tv.aIn || 0))) * (1 + (tv.in5Trend === undefined ? 0.0012 : tv.in5Trend) * idx);
          pr.bd = Math.min(13.5, (base.bd - 0.5 * dc + (tv.bd || 0) - (tv.bdTrend === undefined ? 0.03 : tv.bdTrend) * Math.min(idx, 25)) / k);
          pr.kicks = Math.max(styleOut === 'br' ? 1 : 2, base.kicks - dc + (tv.kicks || 0) - (idx > 6 && styleOut !== 'br' ? 1 : 0));
          const push = vNext * (base.aPush - 0.04 * dc + (tv.aPush || 0));
          pr.uw = pr.bd / push;
          const bo = base.boFactor * vNext;
          tp = Object.assign(pr, { out15: pr.feetOff + pr.uw + (15 - pr.bd) / bo, vBo15: bo });
          kind = styleIn !== styleOut ? 'change' : open ? 'open' : 'flip';
        }
        t[wEnd] = t[last] + tp.in5;
        const turnTime = kind === 'flip' ? tp.lastGap + tp.feetOff : tp.feetOff;
        turns.push({ wall: wEnd, kind, styleIn, styleOut, in5: tp.in5, out15: tp.out15, inOut: tp.in5 + tp.out15, feetOff: tp.feetOff, lastGap: tp.lastGap,
          turnTime, uw: tp.uw, kicks: tp.kicks, bd: tp.bd, vUw: tp.bd * k / tp.uw, vBo15: tp.vBo15 * k, leg: legOfLength(L + 1), ex: tp.ex });
      } else {
        const fin = Object.assign({}, STY[styleIn].finish, v.finish || {});
        const vIn = segs.length ? speed(L, segs.length - 1, s) : s;
        const last5 = 5 / (vIn * (fin.aFin + (v.finAdd || 0)));
        t[wEnd] = t[last] + last5;
        res = { last5, lastGap: fin.lastGap };
      }
    }
    return { t, final: t[D], cleanTime, turns, start: Object.assign({}, sp, { entryT, bt, t15: t[15], bo15Vel: bo15Start * k }),
      finish: res, scale: s, speed, styleOfLength, legOfLength, segsOf, inner, k, pre };
  }
}

/** The analysis of a simulated race, the way the report computes it. */
function analyse(ev, v, sim) {
  const D = ev.distance, P = ev.pool, k = sim.k, cps = sim.pre.list;
  const legLen = ev.relay ? D / 4 : D;
  const styleAtD = d => sim.styleOfLength(Math.min(D / P - 1, Math.floor((d - 1e-6) / P)));
  const srOf = {};
  for (let L = 0; L < D / P; L++) {
    const style = sim.styleOfLength(L);
    const segs = sim.segsOf(L);
    const unitLen = ev.relay ? legLen : ev.style === 'im' ? D : D;
    const base = ev.style === 'im' && !ev.relay ? IM_SR[D <= 100 ? 100 : D <= 200 ? 200 : 400][style] : srBase(style, unitLen);
    segs.forEach(([a, b], i) => {
      const vel = (b - a) / (sim.t[b] - sim.t[a]);
      const ref = sim.speed(L, 0, sim.scale) / (segs.length === 3 ? 1.02 : segs.length === 2 ? 1.03 : 1);
      // Faster lengths with a higher rate, the end of the distance races with a higher rate still.
      const imf = ev.style === 'im' && !ev.relay ? IM_F[D <= 100 ? 100 : D <= 200 ? 200 : 400][style] * ((v.styleF || {})[style] || 1) : 1;
      const rel = vel / (sim.scale * STY[style].speed * imf * (ev.relay ? (v.legs || [1, 1, 1, 1])[sim.legOfLength(L)] : 1));
      srOf[b] = base * (1 + 0.75 * (rel - 1)) + (v.srAdd || 0);
    });
  }
  const rows = [];
  cps.forEach((d, idx) => {
    const prevD = idx === 0 ? 0 : cps[idx - 1];
    const seg = sim.t[d] - (idx === 0 ? 0 : sim.t[prevD]);
    const lastWall = Math.floor((prevD + 1e-9) / P) * P;
    const isSwim = prevD >= lastWall + 15;
    const isFree = isSwim && d <= lastWall + P - 5;
    const style = styleAtD(d), ppc = PPC(style);
    const row = { d, prevD, t: sim.t[d], seg, vel: (d - prevD) * k / seg, isFree, style, ppc, strokes: null, sr: null, sl: null, dps: null, tempo: null, si: null,
      leg: ev.relay ? Math.min(3, Math.floor((d - 1e-6) / legLen)) : 0 };
    if (isFree) {
      const sr = srOf[d];
      row.strokes = half(seg * sr / 60 * ppc);
      row.sr = sr; row.sl = row.vel * 60 / sr; row.dps = row.sl / ppc; row.tempo = 60 / sr; row.si = row.vel * row.sl;
    }
    rows.push(row);
  });
  // Strokes of the other segments (for the 50s, the ΔΑΚ and the Swolf), at the rate of the nearest clean segment.
  const turnAt = {};
  sim.turns.forEach(tn => { turnAt[tn.wall] = tn; });
  rows.forEach((r, i) => {
    if (r.strokes !== null) return;
    const near = rows.slice(Math.max(0, i - 4), i + 5).filter(x => x.sr !== null);
    const sr = near.length ? near[near.length - 1].sr : 40;
    const wall = Math.floor((r.prevD + 1e-9) / P) * P;
    let swimTime;
    if (r.prevD === 0) swimTime = sim.t[r.d] - sim.start.bt;
    else if (r.d % P === 0) swimTime = r.seg - (r.d === D ? sim.finish.lastGap : turnAt[r.d].lastGap);
    else { const tn = turnAt[wall]; swimTime = r.d === wall + 15 ? r.seg - tn.feetOff - tn.uw : r.seg; }
    r.strokes = half(Math.max(0, swimTime) * sr / 60 * r.ppc);
  });
  const byD = {};
  rows.forEach(r => { byD[r.d] = r; });
  const tAt = d => d === 0 ? 0 : sim.t[d];
  // The 50s with the ΔΑΚ: (strokes + seconds from +15 to the wall, every length) per 10 m of the windows.
  const blocks = [];
  for (let b = 0; b < D; b += 50) {
    const e = b + 50;
    const inB = rows.filter(r => r.prevD >= b - 1e-9 && r.d <= e + 1e-9);
    const time = tAt(e) - tAt(b);
    const strokes = inB.reduce((s, r) => s + r.strokes, 0);
    let wS = 0, wT = 0, width = 0;
    for (let w = b; w < e - 1e-9; w += P) {
      const a15 = w + 15, aw = Math.min(w + P, e);
      wS += rows.filter(r => r.prevD >= a15 - 1e-9 && r.d <= aw + 1e-9).reduce((s, r) => s + r.strokes, 0);
      wT += tAt(aw) - tAt(a15);
      width += aw - a15;
    }
    const div = width * k / 10;
    let cyc = 0, ct = 0, cd = 0, cs = 0;
    inB.filter(r => r.sr !== null).forEach(r => { const c = r.sr * r.seg / 60; cyc += c; ct += r.seg; cd += (r.d - r.prevD) * k; cs += c * r.ppc; });
    const sr = ct > 0 ? cyc * 60 / ct : null, sl = cyc > 0 ? cd / cyc : null, vClean = ct > 0 ? cd / ct : null;
    blocks.push({ from: b, to: e, time, cum: tAt(e), strokes, swolf: time + strokes, dak: (wS + wT) / div, dakStrokes: wS / div, dakTime: wT / div,
      winStrokes: wS, winTime: wT, sr, sl, dps: cs > 0 ? cd / cs : null, vel: 50 * k / time, vClean, tempo: sr ? 60 / sr : null, si: vClean && sl ? vClean * sl : null,
      style: styleAtD(e), leg: ev.relay ? Math.min(3, Math.floor((e - 1e-6) / legLen)) : 0,
      first25: P === 50 ? rows.find(r => r.isFree && r.prevD === b + 15) || null : null,
      second25: P === 50 ? rows.find(r => r.isFree && r.d === b + 45 && r.prevD === b + 25) || null : null });
  }
  // The start.
  const s = sim.start;
  const start = { reaction: s.reaction, block: s.block, flight: s.flight, entryT: s.entryT, entryDist: s.entryDist, entryVel: s.entryVel,
    sigEntryVel: s.entryDist * k / s.entryT, kicks: s.kicks, bt: s.bt, uwDist: s.bd - s.entryDist, bd: s.bd,
    entryBoVel: (s.bd - s.entryDist) * k / (s.bt - s.entryT), sigBoVel: s.bd * k / s.bt, t15: s.t15, bo15Vel: s.bo15Vel, style: sim.styleOfLength(0) };
  const lastX = [15, 25, 35].find(x => byD[D - x]) || 5;
  const finish = { last5: sim.finish.last5, lastGap: sim.finish.lastGap, lastX, lastXt: sim.t[D] - sim.t[D - lastX], vLast5: 5 * k / sim.finish.last5,
    style: sim.styleOfLength(D / P - 1) };
  // The phases (start, swims, turns or exchanges, finish), as swimV2Phases_.
  const phases = [];
  const span = (a, b) => tAt(b) - tAt(a);
  phases.push({ kind: 'start', from: 0, to: 15, t: span(0, 15) });
  let prev = 15;
  for (let w = P; w < D; w += P) {
    const tn = turnAt[w];
    phases.push({ kind: 'swim', from: prev, to: w - 5, t: span(prev, w - 5), style: styleAtD(w) });
    const to = Math.min(w + 15, D - 5);
    phases.push({ kind: tn.kind === 'exchange' ? 'exchange' : 'turn', from: w - 5, to, wall: w, t: span(w - 5, to) });
    prev = to;
  }
  phases.push({ kind: 'swim', from: prev, to: D - 5, t: span(prev, D - 5), style: styleAtD(D) });
  phases.push({ kind: 'finish', from: D - 5, to: D, t: span(D - 5, D) });
  const sum = kd => phases.filter(x => x.kind === kd).reduce((a, x) => a + x.t, 0);
  const composition = { start: sum('start'), swim: sum('swim'), turn: sum('turn'), exchange: sum('exchange'), finish: sum('finish') };
  let cyc = 0, ct = 0, cd = 0;
  rows.filter(r => r.sr !== null).forEach(r => { cyc += r.sr * r.seg / 60; ct += r.seg; cd += (r.d - r.prevD) * k; });
  const avg = { sr: cyc * 60 / ct, sl: cd / cyc, vClean: cd / ct };
  avg.tempo = 60 / avg.sr;
  avg.si = avg.vClean * avg.sl;
  avg.dak = blocks.reduce((a, b) => a + b.dak, 0) / blocks.length;
  avg.dakStrokes = blocks.reduce((a, b) => a + b.dakStrokes, 0) / blocks.length;
  avg.dakTime = blocks.reduce((a, b) => a + b.dakTime, 0) / blocks.length;
  const realTurns = sim.turns.filter(x => x.kind !== 'exchange');
  avg.turnInOut = realTurns.length ? realTurns.reduce((a, x) => a + x.inOut, 0) / realTurns.length : null;
  // Relay legs.
  let legs = null;
  if (ev.relay) {
    legs = [0, 1, 2, 3].map(i => {
      const from = i * legLen, to = from + legLen;
      const bl = blocks.filter(b => b.from >= from && b.to <= to);
      const fr = rows.filter(r => r.sr !== null && r.prevD >= from && r.d <= to);
      let c2 = 0, t2 = 0, d2 = 0;
      fr.forEach(r => { c2 += r.sr * r.seg / 60; t2 += r.seg; d2 += (r.d - r.prevD) * k; });
      const ex = i > 0 ? turnAt[from] : null;
      return { leg: i, style: styleAtD(to), from, to, split: tAt(to) - tAt(from), cum: tAt(to), dak: bl.reduce((a, b) => a + b.dak, 0) / bl.length,
        sr: c2 * 60 / t2, sl: d2 / c2, vClean: d2 / t2, ex: ex ? ex.turnTime : null, out15: ex ? ex.out15 : null, reaction: i === 0 ? start.block : null,
        t15: i === 0 ? start.t15 : (ex ? ex.out15 : null) };
    });
  }
  return { ev, v, sim, rows, byD, blocks, start, finish, phases, composition, avg, final: sim.t[D], turns: sim.turns, cps, legs, k, legLen };
}

// ---------------------------------------------------------------------------------------------------------------------------------
// The events and the variants of each: this race, the PB (best time of the athlete or of the team), the reference.

const T = (m, s) => m * 60 + s;
const LC = 'Πισίνα 50μ.', SC = 'Πισίνα 25μ.', SCY = 'Πισίνα 25 γιάρδες';
const EVENTS = [];
function add(pool, unit, style, distance, relay, target) { EVENTS.push({ pool, unit, style, distance, relay: !!relay, target }); }
// 50 m pool
add(50, 'm', 'fs', 50, 0, 22.68); add(50, 'm', 'bk', 50, 0, 25.90); add(50, 'm', 'br', 50, 0, 28.40); add(50, 'm', 'fly', 50, 0, 24.30);
add(50, 'm', 'fs', 100, 0, 48.95); add(50, 'm', 'bk', 100, 0, 55.80); add(50, 'm', 'br', 100, 0, T(1, 2.30)); add(50, 'm', 'fly', 100, 0, 53.60);
add(50, 'm', 'fs', 200, 0, T(1, 49.20)); add(50, 'm', 'bk', 200, 0, T(2, 2.50)); add(50, 'm', 'br', 200, 0, T(2, 15.40)); add(50, 'm', 'fly', 200, 0, T(2, 0.80));
add(50, 'm', 'im', 200, 0, T(2, 3.40)); add(50, 'm', 'fs', 400, 0, T(3, 52.60)); add(50, 'm', 'im', 400, 0, T(4, 21.50));
add(50, 'm', 'fs', 800, 0, T(8, 4.30)); add(50, 'm', 'fs', 1500, 0, T(15, 22.80));
add(50, 'm', 'fs', 400, 1, T(3, 18.40)); add(50, 'm', 'fs', 800, 1, T(7, 20.50)); add(50, 'm', 'im', 400, 1, T(3, 39.80));
// 25 m pool
add(25, 'm', 'fs', 50, 0, 22.10); add(25, 'm', 'bk', 50, 0, 24.60); add(25, 'm', 'br', 50, 0, 27.40); add(25, 'm', 'fly', 50, 0, 23.60);
add(25, 'm', 'fs', 100, 0, 47.80); add(25, 'm', 'bk', 100, 0, 53.30); add(25, 'm', 'br', 100, 0, 59.80); add(25, 'm', 'fly', 100, 0, 52.00);
add(25, 'm', 'im', 100, 0, 54.20);
add(25, 'm', 'fs', 200, 0, T(1, 45.60)); add(25, 'm', 'bk', 200, 0, T(1, 56.80)); add(25, 'm', 'br', 200, 0, T(2, 10.20)); add(25, 'm', 'fly', 200, 0, T(1, 56.40));
add(25, 'm', 'im', 200, 0, T(1, 58.60)); add(25, 'm', 'fs', 400, 0, T(3, 45.20)); add(25, 'm', 'im', 400, 0, T(4, 13.40));
add(25, 'm', 'fs', 800, 0, T(7, 49.50)); add(25, 'm', 'fs', 1500, 0, T(14, 56.00));
add(25, 'm', 'fs', 200, 1, T(1, 29.50)); add(25, 'm', 'im', 200, 1, T(1, 38.60)); add(25, 'm', 'fs', 400, 1, T(3, 13.80)); add(25, 'm', 'im', 400, 1, T(3, 33.20));
add(25, 'm', 'fs', 800, 1, T(7, 5.60));
// 25 yd pool
add(25, 'yd', 'fs', 50, 0, 20.10); add(25, 'yd', 'bk', 50, 0, 22.50); add(25, 'yd', 'br', 50, 0, 25.20); add(25, 'yd', 'fly', 50, 0, 21.60);
add(25, 'yd', 'fs', 100, 0, 44.10); add(25, 'yd', 'bk', 100, 0, 48.40); add(25, 'yd', 'br', 100, 0, 54.50); add(25, 'yd', 'fly', 100, 0, 47.40);
add(25, 'yd', 'im', 100, 0, 49.20);
add(25, 'yd', 'fs', 200, 0, T(1, 37.20)); add(25, 'yd', 'bk', 200, 0, T(1, 46.50)); add(25, 'yd', 'br', 200, 0, T(1, 58.80)); add(25, 'yd', 'fly', 200, 0, T(1, 46.20));
add(25, 'yd', 'im', 200, 0, T(1, 47.80)); add(25, 'yd', 'im', 400, 0, T(3, 52.00));
add(25, 'yd', 'fs', 500, 0, T(4, 24.50)); add(25, 'yd', 'fs', 1000, 0, T(9, 10.00)); add(25, 'yd', 'fs', 1650, 0, T(15, 20.00));
add(25, 'yd', 'fs', 200, 1, T(1, 20.50)); add(25, 'yd', 'im', 200, 1, T(1, 29.80)); add(25, 'yd', 'fs', 400, 1, T(2, 57.50)); add(25, 'yd', 'im', 400, 1, T(3, 15.60));
add(25, 'yd', 'fs', 800, 1, T(6, 32.00));

EVENTS.forEach((ev, i) => {
  ev.seed = 101 + i * 7;
  ev.key = (ev.pool === 50 ? 'lc' : ev.unit === 'yd' ? 'scy' : 'sc') + '-' + (ev.relay ? '4x' + ev.distance / 4 : ev.distance) + '-' + ev.style;
  ev.poolName = ev.pool === 50 ? LC : ev.unit === 'yd' ? SCY : SC;
});

/** The three races of an event. */
function event(ev) {
  const r = rng(ev.seed);
  const newPb = r() < 0.62;
  const dPb = ev.target * (newPb ? 0.002 + 0.004 * r() : -(0.002 + 0.003 * r()));
  const imF = { fly: 1.0, bk: 1.0, br: 1.0, fs: 1.0 };
  const base = { target: ev.target, seed: ev.seed, srAdd: 0, styleF: imF, legs: [1.0, 0.985, 0.993, 1.012], exchanges: [0.22, 0.31, 0.18] };
  const pb = { target: ev.target + dPb, seed: ev.seed + 1, srAdd: -0.6, styleF: { fly: 1.0, bk: 1.005, br: 0.99, fs: 0.995 }, legs: [1.0, 0.99, 0.985, 1.008],
    exchanges: [0.18, 0.26, 0.21], kick: -0.005,
    start: { block: (STYLE_BLOCK[ev.style] || 0.66) + 0.02, bd: (STY[startStyle(ev)].start.bd) - 0.5, kicks: STY[startStyle(ev)].start.kicks - (startStyle(ev) === 'br' ? 0 : 1) },
    turn: { aPush: -0.04, bd: -0.6, aIn: -0.015, kicks: 0 }, finAdd: -0.02, finish: { lastGap: STY[endStyle(ev)].finish.lastGap + 0.06 } };
  const ss = STY[startStyle(ev)].start;
  const ref = { target: ev.target * 0.968, seed: ev.seed + 2, srAdd: 0.9, styleF: imF, legs: [1, 1, 1, 1], exchanges: [0.12, 0.14, 0.10], exBd: 0.8, exKicks: 1,
    start: { reaction: ss.reaction - 0.02, block: ss.block - 0.04, entryDist: ss.entryDist + 0.25, entryVel: ss.entryVel + 0.3, kicks: ss.kicks + (startStyle(ev) === 'br' ? 0 : 1), bd: ss.bd + 1.4, aUw: ss.aUw + 0.06 },
    turn: { aPush: 0.08, bd: 1.2, aIn: 0.05, kicks: 1, bdTrend: 0.015, in5Trend: 0.0006 }, finAdd: 0.04, finish: { lastGap: STY[endStyle(ev)].finish.lastGap - 0.06 } };
  const one = v => analyse(ev, v, simulate(ev, v));
  return { ev, race: one(base), pb: one(pb), ref: one(ref), newPb };
}
const STYLE_BLOCK = { fs: 0.66, bk: 0.58, br: 0.67, fly: 0.66, im: 0.66 };
function startStyle(ev) { return ev.style === 'im' ? (ev.relay ? 'bk' : 'fly') : ev.style; }
function endStyle(ev) { return ev.style === 'im' ? 'fs' : ev.style; }

module.exports = { EVENTS, event, presets, STYLE_NAME, IM_ORDER, MEDLEY_RELAY, PPC, YD };

if (require.main === module) {
  const fmt = x => x < 60 ? x.toFixed(2) : Math.floor(x / 60) + ':' + (x % 60 < 10 ? '0' : '') + (x % 60).toFixed(2);
  const only = process.argv[2];
  EVENTS.filter(ev => !only || ev.key.indexOf(only) >= 0).forEach(ev => {
    const x = event(ev), a = x.race;
    console.log(ev.key.padEnd(16), fmt(a.final), 'pb', fmt(x.pb.final), 'ref', fmt(x.ref.final), '| t15', a.start.t15.toFixed(2), 'bd', a.start.bd.toFixed(1),
      '| v', a.avg.vClean.toFixed(2), 'sr', a.avg.sr.toFixed(1), 'sl', a.avg.sl.toFixed(2), 'dak', a.avg.dak.toFixed(2),
      '| 50s', a.blocks.slice(0, 8).map(b => b.time.toFixed(2)).join(' '),
      a.turns.length ? '| turn ' + a.turns[0].kind + ' ' + a.turns[0].in5.toFixed(2) + '/' + a.turns[0].out15.toFixed(2) + ' bd ' + a.turns[0].bd.toFixed(1) : '',
      a.legs ? '| legs ' + a.legs.map(l => fmt(l.split)).join(' ') : '');
  });
}
