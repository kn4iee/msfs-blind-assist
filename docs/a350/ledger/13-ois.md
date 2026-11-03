# Plan 13 ledger: "Airbus A350 Manual 1.5", PDF pages 31-40 (the OIS)

Classes as plan 12's ledger (`12-front.md`), with **reference** for text with nothing to operate. "The OIS window" is MSFSBA's OIS
window (input Shift+T the captain's, Ctrl+Shift+T the first officer's, over `coherent-a350-ois-agent.js`), the same agent the
probe's `ois` verb reads with.
"Plan 06" is its 1.2.8 evidence (`docs/a350/evidence/06-ois.md`: every pinned page re-captured on 1.2.8, every element read, no
field untitled).

| PDF page | Item (the manual's words) | Where in MSFSBA | Class | Evidence | Owning plan |
| --- | --- | --- | --- | --- | --- |
| 31 | The OIS on each side's outboard display; powered or not | the OIS window, either side; "OIS NOT AVAIL" read on batteries | proven live | plan 06 Task 3, 2026-11-02, 1.2.8 | |
| 31 | FLT OPS MENU, the home page: MISSION, DOCUMENTATION, PERFORMANCE, UTILITIES | the OIS window's menu: each heading and button | proven live | 2026-11-03, 1.2.8: the menu read whole (plan 13, `ois cpt FLT OPS MENU`) | |
| 31 | The shortcut bar at the bottom, always visible | the window's page buttons (GROUND, LOADSHEET, OFP, T.O PERF, LDG PERF, TERML CHART, ENROUTE) and the EFB menu | proven live | read on every page, 2026-11-03 | |
| 32 | FLT OPS PLAN: complete the fields to request a flight plan from SimBrief (the user ID in OPTIONS) | the OIS window, FLT OPS PLAN | built but not proven (the request) | the page reads (plan 06's `plan` page, 1.2.8); a request needs the owner's SimBrief account, never entered by MSFSBA | owner |
| 32 | FLT OPS STS: flight details typed or imported from SimBrief | the OIS window, FLT OPS STS: SIMBRIEF IMPORT, ACFT TYPE, ACFT REG., FLT NBR, CALLSIGN, FROM, TO | proven live (read); proven live (import) | read 2026-11-03, 1.2.8; SIMBRIEF IMPORT used for the VCBI-OMDB flights (2026-10-30, 31) | |
| 33 | LOADSHEET: fuel and load set by hand or imported from SimBrief | the OIS window, LOADSHEET | proven live | plan 06 (1.2.8); "FUEL LOAD, KG=15000 > SET FUEL > Instant Refuel" every flight since 2026-10-30 | |
| 33 | TERML CHARTS 1: search box, departure and arrival airport | the OIS window, TERML CHART: the field and the airports as buttons | proven live | plan 06's `terml_chart` (1.2.8) | |
| 33 | 2 chart list, 3 scroll bar, 5 chart category | the chart list and categories as buttons; the list is read whole, so the scroll bar is not needed | proven live | `docs/inibuilds-a350.md` "The OIS window" | |
| 33 | 4 bookmark pin | the pin buttons, named | proven live | the same | |
| 33 | 6 chart scroll controls and reset; 7 day/night toggle | the icon buttons, named (Zoom in, Pan left, Recenter...) | proven live | the same | |
| 33 | The chart itself | "Chart picture (not readable)" | gap (a picture) | the chart is a raster image; MSFSBA's AI display read could describe it | owner |
| 34 | The bookmark pin beside a category shows the bookmarked charts | the pin button | proven live | the same | |
| 34 | ENROUTE charts and the display-options button | the OIS window, ENROUTE; "Map picture (not readable)"; its buttons named | proven live (buttons); gap (the map) | plan 06's `enroute` (1.2.8) | owner |
| 34 | FLT OPS CLEAR / TURNAROUND: clears the plan and resets the OIS | the button; its TURNAROUND START alert said once with CANCEL and START | proven live | plan 06 Task 3 (cancelled) | |
| 35 | T.O PERF 1 manual airport, 2 runway, 3 sync from the FMS, 4 sync from SimBrief, 5 TORA, 6 weather from the METAR, 7 units, 8 COMPUTE; SEND TO FMGS | the OIS window, T.O PERF, every field by its title, SYNC FMS, COMPUTE, SEND TO FMS | proven live | every flight since 2026-10-30 ("T.O PERF > SYNC FMS", "WIND, OAT, QNH, TOW > COMPUTE > SEND TO FMS"); plan 06 (1.2.8) | |
| 35 | SYNC cannot tell DRY from WET: select it | the runway condition choice | proven live | plan 06's `toperf_rwy_ok` (1.2.8) | |
| 36 | LDG PERF, as T.O PERF | the OIS window, LDG PERF | proven live | plan 06's `ldgperf` (1.2.8) | |
| 37 | AOC: the loadsheet and messages | the OIS window, AOC: the tree, each message by row, the open one line by line | proven live | plan 06's `aoc` (1.2.8: METAR and TAF) | |
| 37 | GROUND: doors, slides, lights, stairs, chocks, GPU, ASU, ACU, GSX, support vehicles | the OIS window, GROUND, each as words ("GPU: Connected") | proven live | plan 06 Task 3 (1.2.8) | |
| 38 | MAINTENANCE: access panels and maintenance actions | the OIS window, MAINTENANCE | proven live | plan 06's `maintenance` (1.2.8); RESET BRAKE TEMP used (campaign X9) | |
| 38 | SITUATIONS: enable an abnormal situation | the OIS window, Situation Manager: the master select (ENABLED/DISABLED) and six situations, each with its description and state | proven live (read); built but not proven (enabling) | read 2026-11-03, 1.2.8; "Once enabled a situation cannot be reset and the flight must be restarted": not enabled | owner |
| 39 | FAILURES: single or multiple failures | the OIS window, FAILURES | proven live | PRIM 1 FAULT and the AC 1/DC 1 cascade (`docs/inibuilds-a350.md` "Manual coverage"); plan 06's `failures` (1.2.8) | |
| 39 | AUTOSAVE: automatic and manual saves | the OIS window, AUTOSAVE: SAVE NOW, the autosaves, custom saves with EDIT and DELETE | proven live | read 2026-11-03, 1.2.8; saves restored from it on 2026-10-31 and 11-03 | |
| 40 | PANEL STATES: pick a state from the list, wait | the OIS window, PANEL STATES (expanded): COLD AND DARK, ON GROUND POWER, ON APU, READY FOR TAKEOFF | proven live (read); proven live (applied) | read 2026-11-03, 1.2.8; applied in the campaign (X9, 2026-10-29) | |
| 40 | OPTIONS | the OIS window, OPTIONS | (plan 23's pages, PDF 41-51) | | 23 |
| 31 | DOCUMENTATION: OPS LIBRARY | the menu button | gap | pressed 2026-11-03, 1.2.8: the page stayed on FLT OPS MENU; the OIS's code builds an "Ops Library" component | 06 |
