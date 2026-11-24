// rv2_races.js (2.22.0): fictional races in the shape Kinovea sends them to the script (the marks: strokes, kicks, breakouts, feet off
// the wall, entries; the distances and speeds measured on the video in 'manual'), from the simulations of the report templates
// (report_templates/sim2.js: every event, the race, its PB and its reference). No real athlete: fictional names and numbers only.
'use strict';
const path = require('path');
const sim2 = require(path.join(__dirname, '../../report_templates/sim2.js'));

const r3 = x => Math.round(x * 1000) / 1000;

/** The marks of one simulated race (sim2 event(ev).race / .pb / .ref) as the payload of the script. */
function payload(ev, x, id, opts) {
  opts = opts || {};
  const sim = x.sim, D = ev.distance, P = ev.pool, k = sim.k;
  const t = d => sim.t[d];
  const strokes = [], kicks = [], breakouts = [], feetOff = [], entries = [];
  const s = sim.start;
  feetOff.push(r3(s.block));
  entries.push(r3(s.entryT));
  breakouts.push(r3(s.bt));
  for (let i = 1; i <= s.kicks; i++) kicks.push(r3(s.entryT + (s.bt - s.entryT) * i / (s.kicks + 1)));
  const turnAt = {};
  sim.turns.forEach(tn => { turnAt[tn.wall] = tn; });
  sim.turns.forEach(tn => {
    const push = t(tn.wall) + tn.feetOff;
    feetOff.push(r3(push));
    const bo = push + tn.uw;
    breakouts.push(r3(bo));
    for (let i = 1; i <= tn.kicks; i++) kicks.push(r3(push + tn.uw * i / (tn.kicks + 1)));
  });
  // The strokes of each segment, evenly in its swimming part; the last one of a length at its gap to the wall.
  x.rows.forEach(r => {
    const n = Math.max(0, Math.round(r.strokes));
    if (!n) return;
    const wall = Math.floor((r.prevD + 1e-9) / P) * P;
    let from = r.prevD === 0 ? 0 : t(r.prevD), to = t(r.d);
    if (r.prevD === 0) from = s.bt;
    else if (r.prevD === wall && wall > 0) from = t(wall) + turnAt[wall].feetOff + turnAt[wall].uw;
    if (r.prevD === wall + 15 && wall > 0 && from < t(wall) + turnAt[wall].feetOff + turnAt[wall].uw) from = t(wall) + turnAt[wall].feetOff + turnAt[wall].uw;
    const atWall = Math.abs(r.d / P - Math.round(r.d / P)) < 1e-9;
    const gap = atWall ? (r.d === D ? sim.finish.lastGap : turnAt[r.d].lastGap) : 0;
    if (atWall) {
      // The last at the gap before the wall, the others evenly before it.
      const last = to - gap;
      for (let i = 0; i < n; i++) strokes.push(r3(n === 1 ? last : from + (last - from) * (i + 1) / n));
    } else {
      for (let i = 0; i < n; i++) strokes.push(r3(from + (to - from) * (i + 0.5) / n));
    }
  });
  strokes.sort((a, b) => a - b);
  const manual = {
    reaction: r3(s.reaction), entryDistHands: Math.round(s.entryDist * 100) / 100, entryVelHands: Math.round(s.entryVel * 100) / 100,
    breakoutDist: Math.round(s.bd * 100) / 100, finishLastStrokeDist: 1.2,
    sigEntryVel: r3(s.entryDist * k / s.entryT), entryBreakoutVel: r3((s.bd - s.entryDist) * k / (s.bt - s.entryT)), breakout15Vel: r3(s.bo15Vel),
    turns: {}
  };
  sim.turns.forEach(tn => {
    manual.turns[String(tn.wall)] = { underwaterDist: Math.round(tn.bd * 100) / 100, turnTime: null, uwSpeed: r3(tn.vUw), bo15Speed: r3(tn.vBo15) };
  });
  const relay = !!ev.relay;
  return Object.assign({
    id, kind: 'race', style: ev.style, distance: D, relay, pool: P, poolUnit: ev.unit,
    athlete: relay ? 'Ομάδα Δείγμα' : 'Αθλητής Δείγμα', athleteId: relay ? '' : '990001', gender: 'Ανδρών', category: 'Ανδρών', club: 'Όμιλος Δείγμα',
    competition: 'Αγώνας δείγμα', location: 'Πόλη δείγμα', round: 'Τελικός', date: '18.12.2026', lane: 4,
    checkpoints: sim.pre.list.map(d => ({ d, t: r3(t(d)) })), strokes, kicks, breakouts, feetOff, entries, manual, strokeRounding: 0.5
  }, opts.extra || {});
}

/** The reference of an event (the reference race of sim2) as Kinovea sends it: the times at the checkpoints, the strokes if asked. */
function reference(ev, x, withStrokes) {
  return { id: 'ref-' + ev.key, name: 'Αναφορά δείγμα', source: '', gender: 'Ανδρών', style: ev.style, relay: !!ev.relay, distance: ev.distance,
    pool: ev.pool, poolUnit: ev.unit,
    times: x.sim.pre.list.map(d => { const p = { d, t: r3(x.sim.t[d]) }; if (withStrokes) { const r = x.byD[d]; if (r) p.n = Math.round(r.strokes * 2) / 2; } return p; }) };
}

/** Every event of the templates: { ev, race, pb, ref } payloads (the PB and the reference as the script gets them). */
function all() {
  return sim2.EVENTS.map(ev => {
    const x = sim2.event(ev);
    return { ev, race: payload(ev, x.race, 'race-' + ev.key), pb: payload(ev, x.pb, 'pb-' + ev.key), ref: reference(ev, x.ref, true), refTimes: reference(ev, x.ref, false), sim: x };
  });
}

module.exports = { payload, reference, all, EVENTS: sim2.EVENTS };
