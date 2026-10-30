const { makeContext } = require('./mock_gas');
const assert = require('assert');
const athletes = [
  ['unique_id','ΟΝΟΜΑΤΕΠΩΝΥΜΟ','ΦΥΛΟ','ΕΤΟΣ ΓΕΝΝΗΣΗΣ','ΗΛΙΚΙΑ','ΚΑΤΗΓΟΡΙΑ','ΣΩΜΑΤΕΙΟ','EMAIL ΠΡΟΠΟΝΗΤΗ'],
  [100000,'INTERNATIONAL','',2027,0,'#N/A','INTERNATIONAL','x@y'],
  [900001,'ΑΛΦΑ ΔΟΚΙΜΗ','Γυναικών',2004,22,'Γυναικών','Ο.Κ. ΔΟΚΙΜΩΝ',''],
  [900002,'ΒΗΤΑ ΔΟΚΙΜΗ','Γυναικών',2004,22,'Γυναικών','Ο.Κ. ΔΟΚΙΜΩΝ',''],
  [900003,'ΓΑΜΜΑ ΔΟΚΙΜΟΣ','Ανδρών',2004,22,'Ανδρών','Ο.Κ. ΔΟΚΙΜΩΝ',''],
  [900002,'ΒΗΤΑ ΔΟΚΙΜΗ','Γυναικών',2004,22,'Γυναικών','Ο.Κ. ΔΟΚΙΜΩΝ',''],
  ['', '', '', '', '', '', '', ''],
];
const G = makeContext({ athletes });
const call = body => JSON.parse(G.swimDoPost({ postData: { contents: JSON.stringify(body) } }).text);
// setup: token exists already in mock; run setup to create reports + summary + menu trigger
G.swimShowConnectionInfo = () => {};
G.swimSetup();
assert.strictEqual(G._triggers.length, 1, 'menu trigger installed');
G.swimSetup();
assert.strictEqual(G._triggers.length, 1, 'no duplicate trigger');
assert.ok(G._ss.getSheetByName('Αναλύσεις Αγώνων'), 'summary sheet in data spreadsheet');
// athletes
let a = call({ token: 'tok', action: 'athletes', app: 'kinovea-swim' });
assert.ok(a.ok, JSON.stringify(a));
assert.strictEqual(a.athletes.length, 4, 'dedup + skip blanks: ' + a.athletes.length);
assert.strictEqual(a.athletes[0].name, 'ΑΛΦΑ ΔΟΚΙΜΗ');
assert.strictEqual(a.athletes.find(x => x.id === '900001').club, 'Ο.Κ. ΔΟΚΙΜΩΝ');
// try-handle ignores non-swim posts
assert.strictEqual(G.swimTryHandlePost({ postData: { contents: JSON.stringify({ foo: 1 }) } }), null);
assert.ok(G.swimTryHandlePost({ postData: { contents: JSON.stringify({ app: 'kinovea-swim', token: 'tok', action: 'ping' }) } }));

// wrong token
let r = call({ token: 'bad', action: 'ping' });
assert.strictEqual(r.ok, false);
// ping
r = call({ token: 'tok', action: 'ping' });
assert.ok(r.ok, JSON.stringify(r));

// live sequence: first only header, then progressively more events, then rename athlete, then pdf
const race = { id: 'r1', athlete: '', style: 'br', distance: 100, pool: 50, round: 'Final', competition: 'EJC', date: '2.7.2026',
  checkpoints: [15,25,35,45,50,65,75,85,95,100].map(d => ({ d, t: null })), strokes: [], kicks: [], breakouts: [], feetOff: [], entries: [], manual: {} };
r = call({ token: 'tok', action: 'update', race });
assert.ok(r.ok, JSON.stringify(r)); assert.ok(r.rebuilt);
const firstName = r.sheetName;
const times = [6.68,12.66,18.78,24.87,27.89,35.1,41.4,47.8,54.2,57.6];
for (let i = 0; i < times.length; i++) {
  race.checkpoints[i].t = times[i];
  for (let k = 0; k < 5; k++) race.strokes.push(+(times[i] - 1 + k * 0.2).toFixed(2));
  race.strokes.sort((a, b) => a - b);
  r = call({ token: 'tok', action: 'update', race });
  assert.ok(r.ok, JSON.stringify(r)); assert.strictEqual(r.rebuilt, false);
}
race.athlete = 'ΑΛΦΑ ΔΟΚΙΜΗ'; race.athleteId = '900001'; race.club = 'Ο.Κ. ΔΟΚΙΜΩΝ'; race.category = 'Γυναικών'; race.birthYear = '2004'; race.feetOff = [0.7, 28.3]; race.breakouts = [5.2, 31.0]; race.kicks = [2, 2.4, 29, 29.4]; race.entries = [0.98];
race.manual = { reaction: 0.26, underwaterDist: 12.17, turns: { '50': { underwaterDist: 8.5 } }, finishLastStrokeDist: 0.3 };
r = call({ token: 'tok', action: 'update', race });
assert.ok(r.ok, JSON.stringify(r));
assert.notStrictEqual(r.sheetName, firstName, 'sheet renamed after athlete name typed');
assert.strictEqual(r.turns[0].kicks, 2); assert.ok(Math.abs(r.turns[0].turnTime - 0.41) < 1e-9);
// (2.15.0) the underwater speed of a turn is the program's (breakout distance − body length) / (breakout − push-off), not metres / time
const turnSpeedCell = () => {
  const rep = G._all[Object.keys(G._all).find(k => k !== 'DATA')];
  const sh = rep.getSheets().find(s => s.getName() === r.sheetName);
  const vals = sh.getRange(1, 1, sh.getLastRow(), 13).getValues();
  let hr = -1, hc = -1;
  vals.forEach((row, i) => row.forEach((v, j) => { if (v === 'Υποβρύχια ταχύτητα: αποκόλληση → έξοδος (m/s)') { hr = i; hc = j; } }));
  assert.ok(hr >= 0, 'header of the underwater speed of the turns');
  return vals[hr + 1][hc];
};
assert.strictEqual(turnSpeedCell(), '', 'no speed without the program value (8.5 m / time is not one)');
race.manual.turns['50'].uwSpeed = 2.215;
race.manual.bodyLength = 1.86;
r = call({ token: 'tok', action: 'update', race });
assert.ok(r.ok, JSON.stringify(r));
assert.strictEqual(Number(turnSpeedCell()), 2.215, 'the underwater speed of the program');
delete race.manual.turns['50'].uwSpeed;
// structure change -> rebuild
race.checkpoints = [15,25,45,50,65,75,95,100].map((d, i) => ({ d, t: [6.68,12.66,24.87,27.89,35.1,41.4,54.2,57.6][i] }));
r = call({ token: 'tok', action: 'update', race });
assert.ok(r.ok && r.rebuilt, JSON.stringify(r));
// pdf
r = call({ token: 'tok', action: 'pdf', race });
assert.ok(r.ok && r.pdf && r.pdf.url, JSON.stringify(r));
// second swimmer, same names -> unique sheet name
r = call({ token: 'tok', action: 'update', race: Object.assign({}, race, { id: 'r2' }) });
assert.ok(r.ok); assert.notStrictEqual(r.sheetName, G._ss.getSheets()[1].getName());
// index sheet (reports spreadsheet) holds 2 races; data spreadsheet has no race sheets
const reports = G._all[Object.keys(G._all).find(k => k !== 'DATA')];
const idx = reports.getSheetByName('Αγώνες');
assert.strictEqual(idx.getLastRow(), 3);
assert.ok(!reports.getSheetByName('Sheet1'), 'default sheet removed');
assert.strictEqual(G._ss.getSheets().length, 2, 'KOE App only got the summary sheet');
// summary row linked by unique_id, with pdf url
const sum = G._ss.getSheetByName('Αναλύσεις Αγώνων');
const row = sum.getRange(2, 1, 1, 23).getValues()[0];
assert.strictEqual(row[0], 900001); assert.strictEqual(row[21], 'r1'); assert.ok(/^https:\/\/drive\.google\.com\/file\/d\//.test(row[20]), row[20]);
assert.ok(String(row[19]).includes('#gid='));
// header shows club / id row
const rsheet = reports.getSheets().find(s => s.getName().startsWith('ΑΛΦΑ ΔΟΚΙΜΗ'));
assert.ok(String(rsheet.getRange(6, 1).getValue()).includes('Μητρώο 900001'), rsheet.getRange(6, 1).getValue());
assert.ok(String(rsheet.getRange(5, 9).getValue()).includes('100μ. Πρόσθιο'), rsheet.getRange(5, 9).getValue());
// demo race builds
G.swimUpsertRace_(G.swimDemoRace_());
// all presets end to end
[[50,'fs'],[200,'im'],[400,'fs'],[800,'fs'],[1500,'fs']].forEach(([dist, style], k) => {
  const per = dist <= 200 ? [15,25,35,45,50] : dist <= 400 ? [15,25,45,50] : [15,45,50];
  const cps = []; for (let w = 0; w < dist; w += 50) per.forEach(p => cps.push(w + p));
  const res = call({ token: 'tok', action: 'update', race: { id: 'p' + k, athlete: 'P' + k, style, distance: dist, pool: 50, checkpoints: cps.map(d => ({ d, t: d * 0.6 })) } });
  assert.ok(res.ok, JSON.stringify(res));
});
console.log('E2E OK; reports:', reports.getSheets().map(s => s.getName()).join(' | '));
