# Plan 09 measurements: how the CDU shows a selection (Task 4 step 1), and CLR on an empty scratchpad

2026-10-28, in cruise over the North Atlantic (route to KJFK, FL350), BlueBird 757, MSFSBA closed. Run by Claude at
the owner's request ("use the MCP server and the Coherent debugger to execute plan 09"): keys pressed through the
simconnect MCP's calculator path (`L:B757_CDU_2_<KEY>` set to 1, then 0 in a second call), screens read over the
Coherent debugger in the `BBSTablet` view (the app's tablet agent, `window.__MSFSBA_BBS757.scrape`), as the MCP cannot
read the Comm Bus frames. Only the first officer's CDU was pressed; the captain's stayed on ACT RTE 1 LEGS.

## Pages read, and their cell attributes

| Page (CDU) | Inverse cells | Colours | Selection shown as |
|---|---|---|---|
| ACT RTE 1 LEGS 1/2 (1, and 2) | 0 | g only | — |
| PROGRESS 1/3 (2) | 0 | g only | — |
| APPROACH REF (2) | 0 | g only | — |
| INIT/REF INDEX (2) | 0 | g only | — |
| PERF INIT (2) | 0 | g only | — |
| TAKEOFF REF 1/2 and 2/2 (2) | 0 | g only | — |
| MENU (2) | 0 | g only | the text `<ACT>` after `<FMC` |
| KJFK ARRIVALS 1/6 (2) | 0 | g only | (nothing selected yet) |
| ACT ECON CRZ 2/3 (2) | 0 | g only | — |
| MOD RTE 1 LEGS 1/2 (2, and 1) | 0 | g only | the title's `MOD`, `<ERASE` at L6 |

Size: small (`1`) for the label rows, units and the page counter (the `/` of "1/2" too); large (`0`) for most data.
In the predicted constraints `272/FL350` and `---/------` the `/` is large and the rest small. Size marks no selection.

**Result:** BlueBird's CDU is green only and never showed an inverse cell; a selection is shown in text (`<ACT>`,
and on the real aircraft `<SEL>`), which the window already reads. So `Bbs757CduMarkers.Mark` marks an inverse run
only (no colour or size mark), in case a page not read here uses it.

The MOD page was made by copying waypoint 2 (L2) into the scratchpad and putting it on L1 (direct to N44W040); it was
erased at once (L6 `<ERASE`) and never executed. Both CDUs then showed ACT RTE 1 LEGS with N46W030 first again, and
the first officer's CDU was put back on PROGRESS.

Frames kept as test fixtures: `bbs757-cdu-menu.json` (MENU, `<ACT>`), `bbs757-cdu-legs-mod.json` (MOD RTE 1 LEGS),
`bbs757-cdu-vnav-crz.json` (ACT ECON CRZ 2/3: a label row of 24 dashes on line 5), `bbs757-cdu-progress.json`
(PROGRESS 1/3: the label row `--------------       GPS`, dashes with text, kept by R71).

## CLR on an empty scratchpad

One CLR (held about 0.3 s) on the first officer's empty scratchpad on PROGRESS 1/3: nothing changed (page and
scratchpad as before). So a second long CLR after one that already cleared the scratchpad does nothing, and Alt+C
can send two long CLRs (plan 08 measured that one long CLR clears the whole scratchpad 45 times in 60 and that a
second always clears what is left).
