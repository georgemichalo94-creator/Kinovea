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

console.log('BREAKOUT CHECK OK');
