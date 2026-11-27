// 2.27.1: the check of the breakout (Kinovea 2.41.1), the analyst's choice («Μόνο έλεγχος»): where the speed from the breakout to the 15
// (+15 after a wall) was slower than the swimming after it, Kinovea sends it as measured with manual.breakout15Slow /
// manual.turns[wall].bo15Slow; the PDF writes the speed with ⚠ and one line that says what it means. Without them: no ⚠.
// Fictional races and names only (tests/report_v2/rv2_races.js).
const assert = require('assert');
const { makeContext } = require('./mock_gas');
const { all } = require('./report_v2/rv2_races');

const ATHLETES = [['unique_id', 'ΟΝΟΜΑΤΕΠΩΝΥΜΟ', 'ΦΥΛΟ', 'ΕΤΟΣ ΓΕΝΝΗΣΗΣ', 'ΚΑΤΗΓΟΡΙΑ', 'ΣΩΜΑΤΕΙΟ'], [990001, 'Αθλητής Δείγμα', 'Ανδρών', 2004, 'Ανδρών', 'Όμιλος Δείγμα']];
const BAD = /NaN|undefined|Infinity|\[object/;
const clone = o => JSON.parse(JSON.stringify(o));

function setup() {
  const G = makeContext({ athletes: ATHLETES });
  G.swimShowConnectionInfo = () => {};
  G.swimSetup();
  const call = body => JSON.parse(G.swimDoPost({ postData: { contents: JSON.stringify(Object.assign({ token: 'tok', app: 'kinovea-swim' }, body)) } }).text);
  return { G, call };
}
const pages = (call, race) => {
  const r = call({ action: 'pdf', v2: true, race });
  assert.ok(r.ok && r.v2 && r.v2.html, JSON.stringify(r).slice(0, 300));
  const text = r.v2.html.replace(/<style>[\s\S]*?<\/style>/, '');
  assert.ok(!BAD.test(text), 'no NaN / undefined in the pages: ' + (text.match(BAD) || [''])[0]);
  return text;
};

// 1. A 400 (7 turns): the start and two turns flagged.
{
  const { call } = setup();
  const p = all().find(x => x.ev.key === 'lc-400-fs');
  assert.ok(p, 'a fictional 400');
  const race = clone(p.race);
  race.id = 'check-400';
  const walls = Object.keys(race.manual.turns);
  assert.ok(walls.length >= 7, 'the turns of the 400: ' + walls.length);
  race.manual.breakout15Slow = true;
  race.manual.turns['100'].bo15Slow = true;
  race.manual.turns['250'].bo15Slow = true;
  const text = pages(call, race);
  assert.ok(/Ταχύτητα έξοδος → 15 ⚠/.test(text), 'the start table: the speed to the 15 with ⚠');
  assert.ok(/⚠ Έξοδος → 15 μ\.: πιο αργά από την κολύμβηση μετά/.test(text), 'the line under the start');
  assert.ok(/⚠ Έξοδος → \+15 στις στροφές 100, 250: πιο αργά από την κολύμβηση μετά/.test(text), 'the line under the turns: ' + (text.match(/⚠ Έξοδος → \+15[^<]*/) || [''])[0]);
  const marked = (text.match(/<td>\d+[.,]\d ⚠<\/td>/g) || []).length;
  assert.strictEqual(marked, 2, 'the breakout of the two turns marked in the table');

  // The same race without the flags: no ⚠ of the breakout.
  const plain = clone(p.race);
  plain.id = 'check-400-plain';
  const t2 = pages(call, plain);
  assert.ok(!/Έξοδος → \+?15[^<]*⚠|⚠ Έξοδος|έξοδος → 15 ⚠/.test(t2), 'no ⚠ without the flags');
}

// 2. A 100 (one turn, drawn on the page of the start): the strip of the turn with ⚠ and its line.
{
  const { call } = setup();
  const p = all().find(x => x.ev.key === 'lc-100-fs');
  const race = clone(p.race);
  race.id = 'check-100';
  race.manual.turns['50'].bo15Slow = true;
  const text = pages(call, race);
  assert.ok(/m\/s ⚠<\/text>/.test(text), 'the strip of the turn: its speed to the +15 with ⚠');
  assert.ok(/⚠ Έξοδος → \+15 μ\.: πιο αργά από την κολύμβηση μετά/.test(text), 'the line under the turn');
  assert.ok(!/Ταχύτητα έξοδος → 15 ⚠/.test(text), 'the start not flagged');
}

// 3. (2.27.2) The breakout of the speeds in the technique report, as in Kinovea (its rule of the breakout, 2.41.2): where Kinovea's
// speed to the +15 says the breakout was taken nearer, «τοίχος → έξοδος» is from there, so that the two parts make the whole
// «τοίχος → +15» (until 2.27.1 from the breakout measured: faster than on the panel, the PDF and the video). Fictional numbers.
{
  const G = makeContext({ athletes: ATHLETES });
  const near = (a, b, tol, msg) => assert.ok(Math.abs(a - b) <= tol, msg + ': ' + a + ' vs ' + b);
  const derive = (v, kind, k) => G.swimTechDerive_(Object.assign({}, v), kind, k);

  // The helper: nearer only when Kinovea's speed to the 15 says so, never farther, the measured one without it.
  near(G.swimSpeedsBreakout_(10.6, 1.8, 4.2, 6.9, 1), 10.14, 1e-9, 'moved: 15 − 1.8 × 2.7');
  assert.strictEqual(G.swimSpeedsBreakout_(10.14, 1.8, 4.2, 6.9, 1), 10.14, 'not moved: the measured one');
  assert.strictEqual(G.swimSpeedsBreakout_(10.17, 1.789, 4.2, 6.9, 1), 10.17, 'the rounding of the speed (3 decimals) is not a move');
  assert.strictEqual(G.swimSpeedsBreakout_(9.5, 1.8, 4.2, 6.9, 1), 9.5, 'never farther than measured');
  assert.strictEqual(G.swimSpeedsBreakout_(10.6, null, 4.2, 6.9, 1), 10.6, 'without the speed to the 15: as measured');
  assert.strictEqual(G.swimSpeedsBreakout_(null, 1.8, 4.2, 6.9, 1), null, 'without a breakout: none');
  assert.strictEqual(G.swimSpeedsBreakout_(10.6, 1.8, 7.0, 6.9, 1), 10.6, 'the 15 before the breakout: as measured');
  assert.strictEqual(G.swimSpeedsBreakout_(10.6, 9.0, 4.2, 6.9, 1), 10.6, 'nothing left of the pool: as measured');

  // A turn the rule moved: wall → breakout from 10.14, and the parts make the whole.
  const turn = { breakoutDist: 10.6, breakoutTime: 4.2, out15: 6.9, bo15Vel: 1.8, in5: 2.6 };
  const t = derive(turn, 'turn', 1);
  near(t.segOut, 10.14 / 4.2, 1e-9, 'wall → breakout from the breakout of the speeds');
  near(t.seg15, 1.8, 1e-12, 'breakout → +15 Kinovea\'s');
  near(t.segOut * 4.2 + t.seg15 * (6.9 - 4.2), t.segAll * 6.9, 1e-9, 'the two parts make the whole');
  assert.ok(t.segOut < 10.6 / 4.2 - 0.02, 'slower than from the breakout measured (2.27.1)');
  // Not moved: as before.
  const same = derive({ breakoutDist: 10.14, breakoutTime: 4.2, out15: 6.9, bo15Vel: 1.8 }, 'turn', 1);
  assert.strictEqual(same.segOut, 10.14 / 4.2, 'not moved: from the breakout measured');
  // Without Kinovea's speed to the +15 (an older analysis): both from the breakout measured, as before.
  const old = derive({ breakoutDist: 10.6, breakoutTime: 4.2, out15: 6.9 }, 'turn', 1);
  assert.strictEqual(old.segOut, 10.6 / 4.2, 'an older analysis: wall → breakout as measured');
  near(old.seg15, (15 - 10.6) / 2.7, 1e-12, 'an older analysis: breakout → +15 as measured');
  // A pool in yards (the distances in yd, the speeds in m/s).
  const yd = derive({ breakoutDist: 11, breakoutTime: 4, out15: 7, bo15Vel: 1.5 }, 'turn', 0.9144);
  near(yd.segOut, (15 - 1.5 * 3 / 0.9144) * 0.9144 / 4, 1e-9, 'yards: from the breakout of the speeds');
  near(yd.segOut * 4 + yd.seg15 * 3, yd.segAll * 7, 1e-9, 'yards: the parts make the whole');

  // The start: Kinovea's release → breakout first (it is from the breakout of the speeds already); without it, the same way.
  const st = { breakoutDist: 12, breakoutTime: 5.5, t15: 7.0, blockTime: 0.7, bo15Vel: 2.2 };
  near(derive(st, 'start', 1).segOut, 11.7 / 4.8, 1e-9, 'the start without Kinovea\'s speed: from the breakout of the speeds');
  assert.strictEqual(derive(Object.assign({ pushVel: 2.41 }, st), 'start', 1).segOut, 2.41, 'Kinovea\'s release → breakout first');
  near(derive(Object.assign({}, st, { bo15Vel: 1.9 }), 'start', 1).segOut, 12 / 4.8, 1e-9, 'the start not moved: as measured');
}

console.log('BREAKOUT CHECK OK');
