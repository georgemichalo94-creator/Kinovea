// fm_make.js (2.22.0, validity check): random fictional analyses as Kinovea keeps them (the JSON of a race of a .swim.json: marks on
// the frames of a video, typed values, official times), for the check that Kinovea and the report of the Google Sheet give the same
// numbers from the same marks (fm_ci.ps1 computes them with the program, fm_check.js with the script). Races of every style, distance
// and pool (relays, medley, yards), videos of several frame rates (also slow motion), and training videos (starts and turns); two
// fifths of them as a wrong click or an analysis in progress leaves them (marks missing, twice, before the start, after the finish,
// splits out of order, typed values out of range). Fixed seed: the same analyses every time. No real athlete.
//   node fm_make.js <folder> [count] [seed]
const fs = require('fs');
const path = require('path');

const dir = process.argv[2];
const count = +(process.argv[3] || 200);
let seed = +(process.argv[4] || 20261113);
const rnd = () => (seed = (seed * 1103515245 + 12345) % 2147483648) / 2147483648;
const pick = a => a[Math.floor(rnd() * a.length)];
const chance = p => rnd() < p;
const between = (a, b) => a + rnd() * (b - a);
const round = (v, d) => +v.toFixed(d);

// Timestamps of a frame, milliseconds of a frame on the player, slow motion factor (as SwimTiming).
const TIMINGS = [
  { ats: 20, ms: 20, hs: 1 },           // 50 fps, a timestamp a millisecond
  { ats: 40, ms: 40, hs: 1 },           // 25 fps
  { ats: 3003, ms: 33.3667, hs: 1 },    // 29.97 fps, 90 kHz
  { ats: 1501.5, ms: 16.6833, hs: 1 },  // 59.94 fps, 90 kHz
  { ats: 512, ms: 33.3333, hs: 4 },     // 120 fps kept as 30 (slow motion of a camera)
  { ats: 1000, ms: 8.3333, hs: 1 }      // 120 fps
];

/** As SwimPresets.DefaultCheckpoints. */
function checkpoints(distance, pool, relay) {
  const leg = relay ? distance / 4 : distance;
  let pattern;
  if (pool === 25) pattern = [15, 20, 25];
  else if (leg <= 50 && relay) pattern = [15, 25, 35, 45, 50];
  else if (relay && leg <= 100) pattern = [15, 25, 45, 50];
  else if (relay) pattern = [15, 45, 50];
  else if (distance <= 200) pattern = [15, 25, 35, 45, 50];
  else if (distance <= 400) pattern = [15, 25, 45, 50];
  else pattern = [15, 45, 50];
  const length = pool === 25 ? 25 : 50, result = [];
  for (let wall = 0; wall < distance; wall += length) {
    pattern.forEach(p => {
      const d = wall + p;
      if (d <= distance && result.indexOf(d) < 0) result.push(d);
    });
  }
  return result.sort((a, b) => a - b);
}

function makeCase(i) {
  const timing = pick(TIMINGS);
  const frame = timing.ms / timing.hs / 1000;
  const origin = Math.round(pick([0, 50, 437, 1200]) * timing.ats);
  // The timestamp of the frame nearest to a time (s from the origin), and the time of that frame.
  const stamp = t => origin + Math.round(Math.round(t / frame) * timing.ats);
  const marks = [];
  const mark = (type, t, distance, wall) => {
    const m = { Id: 'm' + i + '_' + marks.length, Type: type, Timestamp: stamp(t), Distance: distance === undefined ? 0 : distance, Wall: wall === undefined ? -1 : wall };
    marks.push(m);
    return m;
  };

  const training = chance(0.25);
  const race = { Id: 'fm' + i, Athlete: 'FUZZ ATHLETE', AthleteId: '', AthleteGender: pick(['', 'Ανδρών', 'Γυναικών']), Kind: training ? 'training' : 'race',
    Style: 'fs', Relay: false, Distance: 100, Pool: 50, PoolUnit: 'm', Competition: 'Fuzz Games', Round: pick(['Τελικός', 'Προκριματικός', '']),
    Date: '13.11.2026', Checkpoints: [], StartTimestamp: origin, Marks: marks, StrokeCounts: {}, Turns: {} };

  // Kicks, a breakout and strokes after a wall (or the start) until a time.
  const cycle = style => (style === 'br' || style === 'fly' ? between(1.0, 1.5) : between(0.55, 0.8));
  const lap = (from, until, style, feetOff, opts) => {
    let breakout = null;
    if (chance(opts.breakout)) {
      breakout = Math.min(from + between(opts.under[0], opts.under[1]), until - 1.2);
      if (breakout > from + 0.6) {
        const kicks = style === 'br' ? Math.floor(rnd() * 2) : Math.floor(rnd() * 9);
        for (let k = 0; k < kicks; k++) mark('Kick', (feetOff || from) + 0.5 + (breakout - (feetOff || from) - 0.7) * (k + 0.5) / Math.max(1, kicks));
        mark('Breakout', breakout);
      } else {
        breakout = null;
      }
    }

    const period = cycle(style);
    for (let s = (breakout === null ? from + 2.5 : breakout) + between(0.3, 0.7); s < until - between(0.1, 0.5); s += period * between(0.97, 1.03)) mark('Stroke', s);
  };

  if (training) {
    race.Style = pick(['fs', 'bk', 'br', 'fly']);
    race.Pool = pick([25, 50]);
    race.StartTimestamp = chance(0.5) ? null : origin;
    let t = between(2, 6);
    const attempts = 1 + Math.floor(rnd() * 4);
    for (let a = 0; a < attempts; a++) {
      if (chance(0.45)) {
        // A start: the signal, the block, the flight, the water.
        mark('Signal', t);
        if (chance(0.7)) mark('Reaction', t + between(0.12, 0.3));
        if (chance(0.6)) mark('HandsOff', t + between(0.4, 0.55));
        if (chance(0.6)) mark('BackFoot', t + between(0.55, 0.66));
        const off = t + between(0.66, 0.85);
        if (chance(0.95)) mark('FeetOff', off);
        if (chance(0.9)) mark('Entry', off + between(0.25, 0.4));
        if (chance(0.7)) mark('TechSplit', t + between(1.5, 2.2), 5);
        if (chance(0.7)) mark('TechSplit', t + between(3.4, 4.6), 10);
        const t15 = t + between(5.8, 8.0);
        if (chance(0.9)) mark('TechSplit', t15, 15);
        lap(t + 1.1, t15 + 3, race.Style, off, { breakout: 0.9, under: [2.2, 4.5] });
        t = t15 + between(25, 50);
      } else {
        // A turn: from 5 before the wall to 15 after it.
        if (chance(0.8)) mark('TechSplit', t, -5);
        const contact = t + between(2.6, 3.6);
        for (let s = t + 0.4; s < contact - 0.5; s += cycle(race.Style)) mark('Stroke', s);
        if (chance(0.6)) mark('RotationStart', contact - between(0.5, 0.9));
        mark('WallContact', contact);
        const off = contact + between(0.25, 1.0);
        if (chance(0.95)) mark('FeetOff', off);
        if (chance(0.7)) mark('TechSplit', contact + between(1.6, 2.4), 5);
        if (chance(0.7)) mark('TechSplit', contact + between(3.8, 5.2), 10);
        const t15 = contact + between(6.2, 9.0);
        if (chance(0.9)) mark('TechSplit', t15, 15);
        lap(off, t15 + 3, race.Style, off, { breakout: 0.9, under: [1.5, 4.0] });
        t = t15 + between(25, 50);
      }
    }

    if (chance(0.4)) race.BodyLength = round(between(1.6, 2.0), 2);
  } else {
    const yards = chance(0.08);
    const relay = !yards && chance(0.12);
    const style = relay ? pick(['fs', 'im']) : pick(['fs', 'fs', 'bk', 'br', 'fly', 'im']);
    const pool = yards ? 25 : pick([50, 50, 25]);
    let distance;
    if (relay) distance = style === 'im' ? pick([200, 400]) : pick([200, 400, 800]);
    else if (style === 'im') distance = pool === 25 ? pick([100, 200, 400]) : pick([200, 400]);
    else distance = pick(yards ? [50, 100, 200] : [50, 100, 200, 400, 800, 1500]);
    race.Style = style; race.Relay = relay; race.Distance = distance; race.Pool = pool; race.PoolUnit = yards ? 'yd' : 'm';
    race.Checkpoints = checkpoints(distance, pool, relay);

    const speed = { fs: 1.9, fly: 1.75, bk: 1.7, br: 1.5, im: 1.7 }[style] * between(0.75, 1.02);
    const laps = distance / pool, leg = relay ? distance / 4 : 0;
    const wallTimes = {};
    let t = 0;
    for (let L = 0; L < laps; L++) {
      const wall = L * pool;
      const exchange = relay && L > 0 && wall % leg === 0;
      const dive = L === 0 || exchange;
      const lapTime = pool / speed * (1 + 0.04 * L / laps) * (dive ? 0.93 : 1) * between(0.99, 1.02);
      const next = t + lapTime;
      // The marks of the checkpoints of the length.
      const times = {};
      race.Checkpoints.filter(d => d > wall && d <= wall + pool).forEach(d => {
        times[d] = d === wall + pool ? next : t + lapTime * Math.pow((d - wall) / pool, 1.06) * between(0.995, 1.005);
        mark('Split', times[d], d);
      });
      wallTimes[wall + pool] = next;

      let off = null;
      if (L === 0) {
        if (chance(0.6)) mark('Reaction', between(0.12, 0.3));
        off = between(0.6, 0.85);
        if (chance(0.9)) mark('FeetOff', off);
        if (chance(0.8)) mark('Entry', off + between(0.25, 0.4));
        lap(t + 1.1, next, style, off, { breakout: 0.9, under: [2.2, 4.8] });
      } else if (exchange) {
        // The change of a relay: the next swimmer leaves the block around the touch.
        off = t + between(-0.02, 0.45);
        if (chance(0.9)) mark('FeetOff', off);
        if (chance(0.6)) mark('Entry', off + between(0.25, 0.4));
        lap(t + 1.2, next, style, off, { breakout: 0.85, under: [2.2, 4.8] });
      } else {
        off = t + between(0.25, 1.1);
        if (chance(0.85)) mark('FeetOff', off);
        lap(off, next, style, off, { breakout: 0.85, under: [1.5, 4.5] });
        if (chance(0.4)) race.Turns[wall] = { UnderwaterDist: round(between(5, 12), 1), TurnTime: chance(0.15) ? round(between(0.7, 1.2), 2) : null, UnderwaterEstimated: chance(0.2) };
      }

      t = next;
    }

    // Typed values and the official times of the results.
    if (chance(0.3)) race.Reaction = round(between(0.55, 0.8), 2);
    if (chance(0.5)) race.UnderwaterDist = round(between(8, 14), 1);
    if (chance(0.2)) race.BreakoutDist = round(between(9, 15), 1);
    if (chance(0.2)) race.FinishLastStrokeDist = round(between(0.3, 2), 1);
    if (chance(0.4)) race.BodyLength = round(between(1.6, 2.0), 2);
    if (chance(0.2)) race.Checkpoints.forEach(d => { if (chance(0.3)) race.StrokeCounts[d] = pick([4, 6, 7, 8.5, 10, 12, 15.5]); });
    if (chance(0.35)) {
      const n = distance >= 100 && chance(0.7) ? distance / 50 : 1;
      race.Official = [];
      for (let k = 1; k <= n; k++) {
        const d = distance * k / n;
        race.Official.push(round((wallTimes[d] !== undefined ? wallTimes[d] : t * k / n) + between(-0.12, 0.12), 2));
      }
    }
    if (chance(0.2)) race.OfficialReaction = round(between(0.55, 0.8), 2);
  }

  // Two fifths as a wrong click or an analysis in progress leaves them.
  let mutation = -1;
  if (chance(0.4)) {
    mutation = Math.floor(rnd() * 16);
    const last = marks.length ? Math.max.apply(null, marks.map(m => m.Timestamp)) : origin;
    const splits = marks.filter(m => m.Type === 'Split');
    if (mutation === 0) race.Marks = marks.filter(() => chance(0.5));
    else if (mutation === 1) race.Marks = marks.filter(m => m.Type !== 'Split' || chance(0.25));
    else if (mutation === 2) race.StartTimestamp = null;
    else if (mutation === 3) marks.slice(0, 12).forEach(m => marks.push(Object.assign({}, m, { Id: m.Id + 'd' })));
    else if (mutation === 4) ['Split', 'Stroke', 'Kick', 'Breakout', 'FeetOff', 'Entry', 'Reaction'].forEach(type => marks.push({ Id: 'neg' + type, Type: type, Timestamp: origin - Math.round(between(1, 400) * timing.ats), Distance: type === 'Split' && race.Checkpoints.length ? race.Checkpoints[0] : 0, Wall: -1 }));
    else if (mutation === 5) ['Stroke', 'Kick', 'Breakout', 'FeetOff', 'Stroke'].forEach((type, k) => marks.push({ Id: 'late' + k, Type: type, Timestamp: last + Math.round((k + 1) * 30 * timing.ats), Distance: 0, Wall: -1 }));
    else if (mutation === 6 && splits.length) { const s = pick(splits); marks.push(Object.assign({}, s, { Id: s.Id + 'x', Timestamp: s.Timestamp + Math.round(60 * timing.ats) })); }
    else if (mutation === 7 && splits.length > 1) { const a = pick(splits), b = pick(splits); const ts = a.Timestamp; a.Timestamp = b.Timestamp; b.Timestamp = ts; }
    else if (mutation === 8) marks.forEach(m => { m.Timestamp = origin + Math.round(500 * timing.ats); });
    else if (mutation === 9) race.Marks = marks.filter(m => m.Type === 'Stroke');
    else if (mutation === 10) { race.Reaction = pick([0, -0.2, 9]); race.UnderwaterDist = pick([0, -3, 60]); race.BreakoutDist = pick([0, -1, 80]); race.BodyLength = pick([0, -1, 12]); race.FinishLastStrokeDist = pick([0, -2, 30]);
      Object.keys(race.Turns).forEach(w => { race.Turns[w] = { UnderwaterDist: pick([0, -4, 45]), TurnTime: pick([0, -1, 20]) }; }); }
    else if (mutation === 11) race.Official = pick([[0], [10, 5, 1], [25.31, 55.2, 80.1], [-3], [59.99, 59.99]]);
    else if (mutation === 12) { race.StrokeCounts = { 7: 3, 15: -2, 25: 400, 9999: 5, 50: 0 }; }
    else if (mutation === 13 && !training) { race.Distance = pick([75, 30, 125, 1]); }
    else if (mutation === 14 && !training) { race.Checkpoints = pick([[10, 25, 50], [50], [5, 10, 15, 20, 25, 30, 35, 40, 45, 50], [25, 15, 50, 35]]); }
    else if (mutation === 15) ['Signal', 'WallContact', 'TechSplit', 'RotationStart', 'HandsOff', 'BackFoot'].forEach((type, k) => marks.push({ Id: 'odd' + k, Type: type, Timestamp: origin + Math.round((200 + 90 * k) * timing.ats), Distance: type === 'TechSplit' ? pick([-5, 5, 10, 15, 7]) : 0, Wall: pick([-1, 50, 999]) }));
  }

  return { i, plausible: mutation < 0, mutation, timing, race };
}

fs.mkdirSync(dir, { recursive: true });
let plausible = 0, training = 0;
const list = [];
for (let i = 0; i < count; i++) {
  const c = makeCase(i);
  if (c.plausible) plausible++;
  if (c.race.Kind === 'training') training++;
  const n = String(i).padStart(4, '0');
  fs.writeFileSync(path.join(dir, 'in_' + n + '.json'), JSON.stringify(c));
  // For the program: the race as a .swim.json keeps it, and the timing of its video (plain numbers).
  fs.writeFileSync(path.join(dir, 'race_' + n + '.json'), JSON.stringify(c.race));
  list.push([n, c.timing.ats, c.timing.ms, c.timing.hs, c.mutation].join(';'));
}

fs.writeFileSync(path.join(dir, 'cases.txt'), list.join('\n') + '\n');

console.log(`fm_make: ${count} fictional analyses (${count - training} races, ${training} training videos; ${plausible} as marked, ${count - plausible} with wrong or missing marks)`);
