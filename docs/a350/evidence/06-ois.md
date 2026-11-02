# Plan 06: the OIS window on 1.2.8

Measured 2026-11-02 on the iniBuilds A350 1.2.8 at VCBI (a fresh flight from the menu, the aircraft on runway 04 with the engines running),
through `A350Probe repl`'s `ois cpt` and `ois fo`, which read each page with the agent the OIS window uses
(`coherent-a350-ois-agent.js`).

## Task 1: the sign-in code in the speech log

`A350SpeechLog.ForTheLog` replaces the word after "User Code" and its colons with "(not logged)" and keeps the rest of the sentence, so the
LOGIN WITH NAVIGRAPH pop-up, read as "User Code: <code>: This window will close automatically when sucessfully logged in.", is logged as
"User Code: (not logged): This window will close…". The pilot still hears the code. Tests: `A350SpeechLogTests` (made-up codes only).

## Task 3: the OIS backlog, re-confirmed live

| Item | Captain's OIS | First Officer's OIS | Result |
| --- | --- | --- | --- |
| Powered and unpowered | "OIS NOT AVAIL" read as the page's one line on batteries (cold and dark, before the APU) | — | said |
| Field titles | every field on GROUND, LOADSHEET, OFP, T.O PERF, LDG PERF, TERML CHART and ENROUTE reads its title ("ZFW, KG", "WIND, °/kt", "LW, T", "VPILOT, kt", "AIRP") | T.O PERF the same, in its own units ("QNH, inHg", "TOW, x1000 LBS") | no field read as "field" |
| A pop-up's text | FLT OPS CLEAR / TURNAROUND: one alert, "TURNAROUND START: Are you sure you want to start the turnaround? This will clear all flight related data. Options are not affected.", then CANCEL and START (cancelled) | — | said once |
| GROUND's equipment | "GPU: Connected", "CHOCKS: Placed", then "GPU: Not connected", "CHOCKS: Removed" after the presses; doors "Closed" | "CHOCKS: Removed", "GPU: Not connected", "Door L1: Armed" | words, not colours |

No fix was needed. The two OIS sides keep their own unit options: the Captain's T.O PERF was metric (hPa, tonnes) on this flight, the First
Officer's imperial (inHg, thousands of pounds).

## Task 5: the capture scripts

`node capture-run.mjs <ois:cpt | ois:fo | fap> <name>` captures a fixture from any shell and folder: the folder from `--out` or
`A350_FIXTURES` (else `fixtures`), the port from `--port` or `COHERENT_PORT` (else 19999). The clean-up the README's sed did is
`capture-args.js`'s, tested in `capture-args.test.js`; `npm test` 113 of 113.

## Task 4: the pinned pages on 1.2.8

Each page a test pins was captured again on the Captain's side with `capture-run.mjs`, in its plain state on this flight, and scraped by the
agent beside its 1.2.7 fixture, each element compared as its kind and its text with the numbers left out (so a value is not a difference).
Every element of every page read; none came back untitled. Kept as `fixtures/<name>_128.html` (18 pages; the OFP read element for element
the same and is not kept, 148 KB).

| Page | Elements (1.2.7 → 1.2.8) | What differs | Why |
| --- | --- | --- | --- |
| enroute, failures, ground, maintenance, ofp, opt_callouts, opt_sound_cabin_options, opt_throttle_calibration, opt_volume_control, situations | the same | nothing | — |
| ldgperf, toperf_rwy_ok, loadsheet, plan | the same count | the units (hPa, T, KG, M against inHg, LBS, FT); "AIRP, OK" against "AIRP"; the plan's Navigraph notice gone | state: this flight's units, the airport not computed, the owner signed in |
| aoc | 23 → 26 | the messages (METAR and TAF for VCBI and OMDB against a flight report) | state |
| opt_simulation | 40 → 39 | no "SIMULATION RATE: 1x" line | state: shown while time compression runs |
| menu_turnaround | 36 → 34 | PANEL STATES collapsed, not open | state; plus the menu change below |
| menu | 29 → 31 | SITUATIONS and OPTIONS in the UTILITIES column | **1.2.8**: both read as buttons |
| terml_chart | 37 → 25 | the flight's airports (VCBI, OMDB) as buttons, no chart categories yet | **1.2.8** (or the search state): both read as buttons |

No agent change was needed. The harness: 113 of 113, `secrets.test.js` over the new fixtures included.

- **A tooling fault found on the way:** `ceval.mjs ois:cpt` (and `ois:fo`) fails on 1.2.8 with "no socket": `coherent.mjs`'s `oisView`
  finds the side's view by opening and closing a socket on it, and Coherent frees a page's one socket about 500 ms after the close (measured),
  so the open that follows at once is refused. `capture-run.mjs` tries the open again 500 ms apart; `coherent.mjs` itself is plan 20's
  (handoff: wait or retry in `oisView`). A view picked by its index (`ceval.mjs iniEfbA350 … 1`) works.
- Not walked: the First Officer's side page by page and the FAP's pages (`A350Probe ois fo`, `fap`): the FO side read its GROUND and T.O
  PERF the same way in Task 3; the FAP's screen was off on this flight and read as "Screen off" with "Wake the screen".

## Still to do

- Task 2 (focus after an action): needs MSFSBlindAssist's OIS window itself, page by page on both sides, then the owner by ear.
