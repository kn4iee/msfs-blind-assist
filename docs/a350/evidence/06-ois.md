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

## Still to do

- Task 2 (focus after an action): needs MSFSBlindAssist's OIS window itself, page by page on both sides, then the owner by ear.
- Task 4 (the fixtures on 1.2.8): re-capturing each pinned page needs the probe's OIS socket released (one inspector socket per page) and a
  walk of every page and the FAP.
