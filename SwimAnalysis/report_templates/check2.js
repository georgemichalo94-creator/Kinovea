// Internal consistency of every fictional race behind the templates (all events, pools, relays).
const { EVENTS, event, presets } = require('./sim2');
let bad = 0, n = 0;
const ok = (c, m) => { if (!c) { bad++; if (bad < 40) console.log('FAIL', m); } };
const near = (a, b, e) => Math.abs(a - b) < (e || 1e-6);
EVENTS.forEach(ev => {
  const x = event(ev);
  ['race', 'pb', 'ref'].forEach(w => {
    const a = x[w], D = ev.distance, P = ev.pool, tag = ev.key + '/' + w;
    n++;
    ok(near(a.final, w === 'race' ? ev.target : a.v.target, 1e-4), tag + ' target ' + a.final);
    ok(near(a.blocks.reduce((s, b) => s + b.time, 0), a.final), tag + ' 50s sum');
    ok(near(Object.values(a.composition).reduce((s, v) => s + v, 0), a.final), tag + ' composition');
    let prev = 0;
    a.rows.forEach(r => {
      ok(r.t > prev, tag + ' increasing ' + r.d); prev = r.t;
      ok(isFinite(r.vel) && r.vel > 0.8 && r.vel < 3.2, tag + ' speed ' + r.d + ' ' + r.vel);
      if (r.sr) { ok(near(r.vel, r.sr * r.sl / 60, 1e-9), tag + ' v=SR×SL ' + r.d); ok(r.sr > 25 && r.sr < 70 && r.sl > 1.2 && r.sl < 3.0, tag + ' SR/SL range ' + r.d + ' ' + r.sr.toFixed(1) + '/' + r.sl.toFixed(2)); }
      ok(r.strokes >= 0, tag + ' strokes ' + r.d);
    });
    // Clean segments only from +15 to −5, in every length.
    a.rows.filter(r => r.sr).forEach(r => { const w0 = Math.floor((r.prevD + 1e-9) / P) * P; ok(r.prevD >= w0 + 15 && r.d <= w0 + P - 5, tag + ' clean ' + r.prevD + '-' + r.d); });
    const lengths = D / P;
    ok(a.rows.filter(r => r.sr).map(r => Math.floor(r.prevD / P)).filter((v, i, arr) => arr.indexOf(v) === i).length === lengths, tag + ' every length has clean swimming');
    ok(JSON.stringify(a.cps) === JSON.stringify(presets(D, P, ev.relay).list), tag + ' checkpoints = presets');
    a.turns.forEach(t => {
      ok(near(t.inOut, a.byD[t.wall + 15].t - a.byD[t.wall - 5].t, 1e-9), tag + ' inOut ' + t.wall);
      ok(t.bd > 4 && t.bd < 16 && t.uw > 0.8 && t.uw < 7 && t.in5 > 1.5 && t.in5 < 4.5 && t.out15 > 4.5 && t.out15 < 12, tag + ' turn ranges ' + t.wall + ' ' + [t.in5, t.out15, t.uw, t.bd].map(v => v.toFixed(2)));
      if (t.kind === 'exchange') ok(t.turnTime > -0.031 && t.turnTime < 0.6, tag + ' exchange ' + t.turnTime);
    });
    ok(near(a.start.t15, a.byD[15].t), tag + ' t15');
    ok(a.start.bt > a.start.entryT && a.start.bd > a.start.entryDist && a.start.bd < 15, tag + ' start order');
    a.blocks.forEach(b => ok(near(b.dak, b.dakStrokes + b.dakTime, 1e-9) && b.dak > 7 && b.dak < 20, tag + ' ΔΑΚ ' + b.from + ' ' + b.dak.toFixed(2)));
    if (a.legs) ok(near(a.legs.reduce((s, l) => s + l.split, 0), a.final), tag + ' legs sum');
  });
});
console.log(bad ? bad + ' problems' : 'all consistent: ' + EVENTS.length + ' events, ' + n + ' races');
