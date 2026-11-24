// Internal consistency of the fictional races behind the templates.
const { R } = require('./data');
let bad = 0;
const ok = (cond, msg) => { if (!cond) { bad++; console.log('FAIL', msg); } };
Object.keys(R).forEach(k => ['race', 'pb', 'ref'].forEach(w => {
  const x = R[k][w], D = x.p.distance, tag = k + '/' + w;
  const near = (a, b, e) => Math.abs(a - b) < (e || 1e-9);
  ok(near(x.blocks.reduce((s, b) => s + b.time, 0), x.final, 1e-6), tag + ' 50s sum');
  ok(near(Object.values(x.composition).reduce((s, v) => s + v, 0), x.final, 1e-6), tag + ' composition sum');
  ok(near(x.phases.reduce((s, p) => s + p.t, 0), x.final, 1e-6), tag + ' phases sum');
  let prev = 0;
  x.rows.forEach(r => { ok(r.t > prev, tag + ' increasing at ' + r.d); prev = r.t; if (r.sr) ok(near(r.vel, r.sr * r.sl / 60, 1e-9), tag + ' v=SR×SL at ' + r.d); });
  ok(near(x.start.t15, x.byD[15].t), tag + ' t15');
  x.turns.forEach(t => {
    ok(near(t.inOut, x.byD[t.wall + 15].t - x.byD[t.wall - 5].t, 1e-9), tag + ' inOut ' + t.wall);
    ok(near(t.in5, x.byD[t.wall].t - x.byD[t.wall - 5].t, 1e-9), tag + ' in5 ' + t.wall);
    ok(near(t.out15, x.byD[t.wall + 15].t - x.byD[t.wall].t, 1e-9), tag + ' out15 ' + t.wall);
    ok(t.bd > 5 && t.bd < 15 && t.uw > 1 && t.uw < 6, tag + ' turn ranges ' + t.wall);
  });
  ok(x.start.bt > x.start.entryT && x.start.bd > x.start.entryDist && x.start.bd < 15, tag + ' start order');
  ok(near(x.finish.last5, x.byD[D].t - x.byD[D - 5].t, 1e-9), tag + ' last 5');
  // Clean segments: SR and SL only there, and where the user wants them.
  const kinds = Array.from(new Set(x.rows.filter(r => r.sr).map(r => (r.prevD % 50) + '-' + ((r.d % 50) || 50))));
  const expect = D <= 200 ? ['15-25', '25-35', '35-45'] : D <= 400 ? ['15-25', '25-45'] : ['15-45'];
  ok(JSON.stringify(kinds) === JSON.stringify(expect), tag + ' clean segments ' + kinds);
}));
console.log(bad ? bad + ' problems' : 'all consistent: ' + Object.keys(R).length * 3 + ' races');
