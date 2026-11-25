// (2.26.0 mockup) The slides of the report: the pages as a presentation at the end of the video (16:9, 3 s each) and the posts for
// the social media (4:5). Every event of the templates with the PB and the reference, with the reference only, with none, and the
// short analysis: built without an error, without NaN / undefined, one slide for each page of the report in its order (the passings
// last), the posts with the race's own numbers only (no PB, no reference). Fictional races and names only (report_v2/rv2_races.js).
'use strict';
const assert = require('assert');
const { makeContext } = require('./mock_gas');
const { all } = require('./report_v2/rv2_races');

const G = makeContext({ athletes: [['unique_id', 'ΟΝΟΜΑΤΕΠΩΝΥΜΟ', 'ΦΥΛΟ'], [990001, 'Αθλητής Δείγμα', 'Ανδρών']] });
const V2 = G.SwimV2;
const BAD = /NaN|undefined|Infinity|\[object|null(?![a-zA-Zά-ώ])/;

function model(p, variant) {
  const race = G.swimNormalizeRace_(JSON.parse(JSON.stringify(p.race)));
  const cmp = {};
  if (variant.pb) {
    const pr = G.swimNormalizeRace_(JSON.parse(JSON.stringify(p.pb)));
    cmp.pb = { race: pr, analysis: G.swimAnalyze_(pr), label: 'PB: δείγμα' };
  }
  if (variant.ref) {
    const er = G.swimReferenceRace_(p.ref);
    cmp.elite = { race: er, analysis: G.swimAnalyze_(er), label: 'Αναφορά δείγμα' };
  }
  return V2.fromScript(race, G.swimAnalyze_(race), cmp);
}

const VARIANTS = [{ name: 'full', pb: true, ref: true }, { name: 'ref', pb: false, ref: true }, { name: 'none', pb: false, ref: false },
  { name: 'short', pb: true, ref: true, short: true }];
let built = 0;
all().forEach(p => VARIANTS.forEach(v => {
  const x = model(p, v);
  const r = V2.slides(x, v.short ? { short: 3 } : {});
  const tag = p.ev.key + '/' + v.name;
  const html = r.html.replace(/<style>[\s\S]*?<\/style>/, '');
  const bad = html.match(BAD);
  assert.ok(!bad, tag + ': ' + (bad ? html.slice(Math.max(0, bad.index - 120), bad.index + 40) : ''));
  // One section for each slide and post, each its own size.
  const video = html.match(/<section class="sl v[ "]/g) || [], social = html.match(/<section class="sl s[ "]/g) || [];
  assert.strictEqual(video.length, r.video, tag);
  assert.strictEqual(social.length, r.social, tag);
  // The slides: the pages of the report in their order (the phases only with a comparison), the passings last (not in the short one).
  assert.strictEqual(r.kinds[0], 'overview', tag);
  if (v.short) assert.ok(r.video <= 4 && r.kinds.indexOf('splits') < 0, tag);
  else assert.strictEqual(r.kinds[r.kinds.length - 1], 'splits', tag);
  if (v.pb || v.ref) assert.ok(r.kinds.indexOf('phases') === 1, tag + ': the comparison by phase second');
  // The counter of each slide.
  for (let i = 1; i <= r.video; i++) assert.ok(html.indexOf(`<span class="sl-n">${i} / ${r.video}</span>`) >= 0, tag + ': slide ' + i);
  // The posts: the race's own numbers, never the PB or the reference.
  const posts = html.slice(html.search(/<section class="sl s[ "]/));
  assert.ok(!/Αναφ\.|αναφορά|Αναφορά|ΑΡ |Ατομικό ρεκόρ|Ρεκόρ ομάδας/.test(posts.replace(/ΑΝΑΛΥΣΗ ΑΓΩΝΑ/g, '')), tag + ': a comparison in the posts');
  assert.ok(r.social >= 4 && r.social <= 7, tag + ': ' + r.social + ' posts');
  // The images Kinovea puts in: the logo on every slide and post, the frame on the cover.
  assert.strictEqual((r.html.match(/src="\{\{LOGO\}\}"/g) || []).length, r.video + r.social, tag);
  assert.ok(r.html.indexOf('{{FRAME}}') > 0, tag);
  built++;
}));
console.log('test_slides: ' + built + ' slide sets OK');
