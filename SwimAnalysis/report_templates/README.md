# Report templates (PDF), one per distance

Templates of the race analysis report for 50, 100, 200, 400 freestyle, 400 medley, 800 and 1500 (50 m pool), built from
fictional races only (`data.js`: one simulation per race, so times, speeds, strokes and turns agree with each other the way
`SwimCalculator` / `swimAnalyze_` compute them; checkpoints as `SwimPresets.DefaultCheckpoints`: 15·25·35·45·50 up to 200,
15·25·45·50 for 400, 15·45·50 for 800 and 1500).

- `node check.js` checks the fictional data (sums, speed = rate × cycle length, turns −5/+15, clean segments).
- `node build.js [key…]` writes `out/<key>.html` and the PDFs (Chromium through Playwright), and reports the free space of
  every page (negative = overflow).

The pages: overview (KPIs, time composition, gains and losses by phase, findings), pace (per 50 / 100, deviation from the
mean, pacing type, variation), strokes (speed, rate, tempo, cycle length; per 25 in the 400s, per 50 in 800 / 1500), per-50
table with ΔΑΚ (800 / 1500), start and finish (start strip and phases, last 100), turns (table in the order of the events,
trends, the average turn), all checkpoints, comparison and glossary. The 400 medley has a page by style.

## 2nd version: every event, ΔΑΚ first, recommendations from the numbers

`build2.js` writes one PDF per event, 66 in all, in a folder per pool: 50 m pool (17 individual events, 4×100 free,
4×100 medley, 4×200 free), 25 m pool (18 individual, 4×50 / 4×100 free and medley, 4×200 free) and 25 yd pool (the yard
events, 500 / 1000 / 1650 free included, and the same relays). All races are fictional.

- `sim2.js` simulates any event: start, breakout, clean swimming, turns, finish and, in relays, the exchanges and each
  leg. Turn and start times scale with the swimmer's speed, so 25 m and yard races come out as real as 50 m ones.
  Checkpoints follow `SwimPresets.DefaultCheckpoints` (15·20·25 in a 25 m / 25 yd pool; relay legs by leg length).
  Every event has three races: this race, the personal best and a reference.
- `recs.js` turns the numbers into recommendations: each one names the facts, the action, a target and the time it is
  worth (half the gap to the reference), sorted by gain; the target time is the result minus the four largest gains, and
  the target splits follow the reference profile. It also writes the three key messages of the first page.
- `pages2.js` builds the pages. ΔΑΚ is the main index: hero card on page 1, its own page (per 50, its parts — strokes and
  time from 15 m to the wall — per style in the medley, against the personal best and the reference). Then the
  recommendations, legs and exchanges (relays), speed or time composition, styles (individual medley), pace and target
  splits, strokes (per 25, per segment or per 50), start and finish, turns and an appendix with every checkpoint. Type is
  larger than in the 1st version (body 11.2 px, key numbers 27 px, hero 60 px).
- `node check2.js` checks all 66 events × 3 races (target times, sums, rising times, speed = rate × cycle length,
  clean segments, checkpoints, turn and exchange ranges, ΔΑΚ = its parts, legs add up to the total).
- `node build2.js [key…]` writes `out2/<pool>/NN - <event>.pdf` and `out2/html/<key>.html`, and fails a page whose
  content runs into the footer or whose table is wider than the page (`out2/report.json`).
