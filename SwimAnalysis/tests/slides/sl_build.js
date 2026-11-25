// sl_build.js (2.39.0): the slides of a few FICTIONAL races (report_v2/rv2_races.js, no real athlete) as the Apps Script 2.26.0 sends
// them to Kinovea with the PDF of a package: <key>.html (the places of the logo and of the frame of the video kept) and <key>.json
// ({ video, social }: the slides of the video and the posts).
//   node sl_build.js <folder> <race key>...
'use strict';
const fs = require('fs');
const path = require('path');
const { makeContext } = require('../mock_gas');
const { all } = require('../report_v2/rv2_races');

const out = process.argv[2];
const keys = process.argv.slice(3);
if (!out || !keys.length) {
  console.error('node sl_build.js <folder> <race key>...');
  process.exit(2);
}
fs.mkdirSync(out, { recursive: true });
const G = makeContext({ athletes: [['unique_id', 'ΟΝΟΜΑΤΕΠΩΝΥΜΟ', 'ΦΥΛΟ'], [990001, 'Αθλητής Δείγμα', 'Ανδρών']] });
let made = 0;
keys.forEach(key => {
  const p = all().find(q => q.ev.key === key);
  if (!p) throw new Error('no fictional race ' + key);
  const race = G.swimNormalizeRace_(JSON.parse(JSON.stringify(p.race)));
  const pr = G.swimNormalizeRace_(JSON.parse(JSON.stringify(p.pb)));
  const er = G.swimReferenceRace_(p.ref);
  const src = { race, analysis: G.swimAnalyze_(race), extras: {
    pb: { race: pr, analysis: G.swimAnalyze_(pr), label: 'PB: δείγμα' }, elite: { race: er, analysis: G.swimAnalyze_(er), label: 'Αναφορά δείγμα' } } };
  const v2 = G.swimV2Pages_(src, { short: false, full: true }, true);
  if (!v2.slides) throw new Error('no slides for ' + key + ': ' + v2.slidesError);
  fs.writeFileSync(path.join(out, key + '.html'), v2.slides.html);
  fs.writeFileSync(path.join(out, key + '.json'), JSON.stringify({ video: v2.slides.video, social: v2.slides.social }));
  console.log('slides ' + key + ': ' + v2.slides.video + ' of the video, ' + v2.slides.social + ' posts, ' + Math.round(v2.slides.html.length / 1024) + ' KB');
  made++;
});
console.log('sl_build: ' + made + ' races');
