// fm_check.js (2.22.0, validity check): the same numbers in Kinovea and in the report. Each fictional analysis of fm_make.js, as the
// program computed and sent it (fm_ci.ps1), goes to the script of the Google Sheet the whole way (the report is written): no error of
// the script (a refusal says what is missing), nothing undefined / NaN in the report, its images and charts whole on their pages; and
// for a race every number the program shows on the video and in its panel (times, speeds, strokes, rates, lengths, turns, start,
// finish, the 50s, the averages) is the number the script puts in the report, from the same marks.
//   node fm_check.js <folder> [survey]      (survey: the differences are listed, only an error fails)
const fs = require('fs');
const path = require('path');
const assert = require('assert');
const { makeContext } = require('../mock_gas');

const dir = process.argv[2];
const survey = process.argv[3] === 'survey';
const G = makeContext({ athletes: [['unique_id', 'ΟΝΟΜΑΤΕΠΩΝΥΜΟ', 'ΦΥΛΟ'], [600001, 'FUZZ ATHLETE', 'Γυναικών']] });
G.swimShowConnectionInfo = () => {};
G.swimSetup();
const { check, badText } = require('../layout_check').makeCheck(G);
const reports = G._all[Object.keys(G._all).find(k => k !== 'DATA')];
const post = body => JSON.parse(G.swimDoPost({ postData: { contents: JSON.stringify(Object.assign({}, body, { token: 'tok', app: 'kinovea-swim' })) } }).text);

const none = v => v === null || v === undefined;
const same = (a, b) => {
  if (none(a) || none(b)) return none(a) && none(b);
  if (typeof a === 'number' && typeof b === 'number') return Math.abs(a - b) <= 1e-6 * Math.max(1, Math.abs(a), Math.abs(b));
  return a === b;
};

// What the program computed (SwimComputed) and what the script analysed (swimAnalyze_), value by value.
const ROW = [['T', 't'], ['SegTime', 'segTime'], ['Vel', 'vel'], ['Strokes', 'strokes'], ['StrokesAuto', 'strokesAuto'], ['StrokesTyped', 'strokesTyped'],
  ['Sr', 'sr'], ['SrExact', 'srExact'], ['Dps', 'dps'], ['Sl', 'sl'], ['Style', 'style'], ['IsSwim', 'isSwim'], ['IsFree', 'isFree']];
const TURN = [['TurnTime', 'turnTime'], ['UnderwaterTime', 'underwaterTime'], ['Kicks', 'kicks'], ['LastStrokeGap', 'lastStrokeGap'], ['InOut', 'inOut'],
  ['Out15', 'out15'], ['In5', 'in5'], ['Pushoff', 'pushoff'], ['Breakout', 'breakout'], ['Exchange', 'exchange'], ['Flip', 'flip'], ['StyleIn', 'styleIn'], ['StyleOut', 'styleOut']];
const BLOCK = [['Time', 'time'], ['Strokes', 'strokes'], ['Swolf', 'swolf'], ['Dak', 'dak'], ['Sr', 'sr'], ['Dps', 'dps'], ['Sl', 'sl'], ['Vel', 'vel'], ['Style', 'style']];
const START = [['BlockTime', 'blockTime'], ['EntryTime', 'entryTime'], ['FlightTime', 'flightTime'], ['StartUnderwaterTime', 'underwaterTime'], ['StartKicks', 'kicks'], ['Start15', 't15']];
const FINISH = [['FinalTime', 'tFinish'], ['FinishLastStrokeGap', 'lastStrokeGap'], ['FinishLast5', 'last5'], ['FinishLast15', 'last15']];
const SUMMARY = [['FinalTime', 'finalTime'], ['AvgVel', 'avgVel'], ['AvgSr', 'avgSr'], ['AvgDps', 'avgDps'], ['AvgSl', 'avgSl'], ['AvgDak', 'avgDak']];

const stats = {};
const differ = (field, meta, at, cs, js) => {
  const s = stats[field] = stats[field] || { compared: 0, differ: 0, asMarked: 0, examples: [] };
  s.compared++;
  if (same(cs, js)) return;
  s.differ++;
  if (meta.plausible) s.asMarked++;
  if (s.examples.length < 4 || (meta.plausible && s.examples.filter(e => e.plausible).length < 3))
    s.examples.push({ plausible: meta.plausible, text: `#${meta.i}${meta.plausible ? '' : ' (mutation ' + meta.mutation + ')'} ${meta.what}${at === '' ? '' : ' @' + at}: Kinovea ${JSON.stringify(cs)} / report ${JSON.stringify(js)}` });
};
const pairs = (section, list, meta, at, cs, js) => list.forEach(p => differ(section + '.' + p[0], meta, at, cs ? cs[p[0]] : undefined, js ? js[p[1]] : undefined));

function compare(meta, c, a) {
  const rows = {};
  (a.rows || []).forEach(r => { rows[r.d] = r; });
  (c.Rows || []).forEach(r => pairs('row', ROW, meta, r.D, r, rows[r.D]));
  differ('rows.count', meta, '', (c.Rows || []).length, (a.rows || []).length);
  const turns = {};
  (a.turns || []).forEach(t => { turns[t.wall] = t; });
  (c.Turns || []).forEach(t => pairs('turn', TURN, meta, t.Wall, t, turns[t.Wall]));
  differ('turns.count', meta, '', (c.Turns || []).length, (a.turns || []).length);
  const blocks = {};
  (a.blocks || []).forEach(b => { blocks[b.to] = b; });
  (c.Blocks || []).forEach(b => pairs('block', BLOCK, meta, b.To, b, blocks[b.To]));
  differ('blocks.count', meta, '', (c.Blocks || []).length, (a.blocks || []).length);
  pairs('start', START, meta, '', c, a.start || {});
  pairs('finish', FINISH, meta, '', c, a.finish || {});
  pairs('summary', SUMMARY, meta, '', c, a.summary || {});
}

const files = fs.readdirSync(dir).filter(f => /^in_\d+\.json$/.test(f)).sort();
assert.ok(files.length >= 50, 'the analyses of fm_make.js: ' + files.length);
const failures = [];
const refusals = {};
let sent = 0, races = 0, trainings = 0, compared = 0, notComputed = 0;
for (const f of files) {
  const meta = JSON.parse(fs.readFileSync(path.join(dir, f), 'utf8'));
  const r0 = meta.race;
  meta.what = `${r0.Kind === 'training' ? 'training ' : ''}${r0.Style} ${r0.Distance}/${r0.Pool}${r0.PoolUnit === 'yd' ? 'yd' : ''}${r0.Relay ? ' relay' : ''}`;
  const outFile = path.join(dir, f.replace('in_', 'out_'));
  try {
    assert.ok(fs.existsSync(outFile), 'not computed by the program (no result)');
    const out = JSON.parse(fs.readFileSync(outFile, 'utf8').replace(/^﻿/, ''));
    if (out.error) { notComputed++; throw new Error('the program: ' + out.error); }

    // The whole way, as Kinovea sends it.
    const r = post(out.body);
    sent++;
    if (!r.ok) {
      // A refusal says what is missing; an error of the script is a failure.
      const message = String(r.error || '');
      assert.ok(message.length > 0 && !/TypeError|ReferenceError|RangeError|SyntaxError|Cannot read|is not a function|is not defined|undefined|Maximum call stack|\bNaN\b/.test(message),
        'the script: ' + JSON.stringify(r).slice(0, 400));
      refusals[message] = (refusals[message] || 0) + 1;
      continue;
    }

    const sheet = reports.getSheets().find(s => s.getSheetId() === r.sheetId);
    assert.ok(sheet, 'the sheet of the report');
    Object.values(sheet._cells).forEach(c => assert.ok(!badText(c.v, false), `the sheet has «${c.v}»`));
    const again = post(out.body);
    assert.ok(again.ok && !again.rebuilt, 'the same analysis again is not rebuilt: ' + JSON.stringify(again).slice(0, 200));

    const rr = G.swimNormalizeRace_(out.body.race);
    const an = G.swimAnalyze_(rr);
    const extras = { pageHeight: w => G.swimPdfPageHeight_(reports, w) };
    if (rr.kind === 'training') extras.techCompare = G.swimTechCompare_(rr);
    check(G.swimBuildLayout_(rr, an, extras), meta.what, false);
    if (rr.kind === 'training') { trainings++; continue; }

    races++;
    compare(meta, out.computed, an);
    compared++;
  } catch (e) {
    failures.push(`#${meta.i} (${meta.what}${meta.plausible ? '' : ', mutation ' + meta.mutation}): ` + String(e && e.message ? e.message : e).split('\n')[0].slice(0, 500));
  }
}

const fields = Object.keys(stats).filter(k => stats[k].differ > 0).sort((x, y) => stats[y].differ - stats[x].differ);
const total = Object.keys(stats).reduce((n, k) => n + stats[k].compared, 0);
console.log(`fm_check: ${sent} analyses sent to the script (${races} races, ${trainings} training videos; refused: ${JSON.stringify(refusals)}), ${compared} races compared, ${total} values`);
fields.forEach(k => {
  const s = stats[k];
  console.log(`DIFF ${k}: ${s.differ} of ${s.compared} (${s.asMarked} in analyses as marked)`);
  s.examples.forEach(e => console.log('     ' + e.text));
});
failures.slice(0, 14).forEach(x => console.log('FAIL ' + x));
const asMarked = fields.reduce((n, k) => n + stats[k].asMarked, 0), all = fields.reduce((n, k) => n + stats[k].differ, 0);
if (failures.length > 0 || (!survey && all > 0)) {
  console.log(`FAILED: ${failures.length} errors, ${all} different values (${asMarked} in analyses as marked)`);
  process.exit(1);
}
console.log(`fm_check ok: ${all === 0 ? 'the same numbers in Kinovea and in the report' : all + ' different values listed (' + asMarked + ' in analyses as marked)'}, no errors`);
