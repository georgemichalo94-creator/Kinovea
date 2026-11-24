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
