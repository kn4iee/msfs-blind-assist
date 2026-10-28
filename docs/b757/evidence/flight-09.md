# Plan 09 measurements: how the CDU shows a selection (Task 4 step 1, then every page), and CLR on an empty scratchpad

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

## The full tour (same day, after the owner asked whether every page and subpage was read)

A script (kept out of the repo) drove the first officer's CDU through the app's tablet agent on the Coherent
debugger: `press(2, key)` for the key, then a scrape; the MCP was not used here because each key would take two calls
and the script must read every screen to choose the next key. It pressed only page keys, NEXT/PREV, and line keys
beside a known navigation prompt ("<INDEX", "POS INIT>", "<ACARS" …), never EXEC or HOLD, never a prompt that sends,
selects, activates or relocates (SEND, REQUEST, ACTIVATE, ENG OUT, RTA, RELOCATE, CROSS LOAD), and would have erased
any MOD page at once (none appeared). Three passes, 62 page reads, about 55 distinct pages:

- the INIT/REF INDEX tree: IDENT, POS INIT 1/4 and POS REF 2/4-4/4, PERF INIT, TAKEOFF REF 1/2-2/2, APPROACH REF,
  REF NAV DATA, ALTN 1/2 and ALTN LIST 2/2, MAINTENANCE INDEX and its SETTINGS page;
- ACT RTE 1 1/3-3/3, ACT RTE 1 LEGS 1/2-2/2, RTE 2 LEGS 1/1, ACT RTE 1 DATA 1/2-2/2 (FMC COMM's WIND), DESCENT FORECAST;
- DEP/ARR INDEX, KJFK ARRIVALS 1/6-6/6, LFBT DEPARTURES 1/2-2/2;
- VNAV CLB 1/3, CRZ 2/3, DES 3/3; PROGRESS 1/3, 2/3, RTA PROGRESS 3/3; POS REPORT; FIX INFO 1/2-2/2; NAV RADIO;
- FMC COMM; ATC LOGON/STATUS and ATC INDEX; MENU, ACARS MENU, ACARS ACFT PERF MENU, ACARS-MAIN OPS MENU,
  BLUEBIRDSIM MENU and its STATS 1/1.

**Result: no inverse cell and no colour but green on any page.** Selections are text everywhere: `<ACT>` after
`<FMC` on MENU, `LMB3M <ACT>  <ACT>   02` on LFBT DEPARTURES (the active SID and runway), `RTE 1 (ACT)` on the
DEP/ARR INDEX; ACARS MENU shows `PERF*`. Glyphs other than ASCII: `°`; `☐` (entry boxes: RTA PROGRESS, FIX INFO, which
the parser reads as `_`); and arrows, `↓` on SETTINGS (`↓WET`, `↓<1/8IN`) and `←` `→` on POS REF 3/4 and REF NAV
DATA, which the window passes through as the Fenix window does its arrows. Pressing MENU's ACMS, HUD and MEMORY,
ACARS MENU's MAINT, and MAINTENANCE INDEX's AIRLINE POLICY, IRS MONITOR, SENSORS and DISCRETES opened no page.
Full rows of dashes sit in label rows on most pages (line 6's label, sometimes line 5's), which R71 drops; dash rows
with text (`--------RTE 2 ----------`, `---------------DATA LINK`) stay.

One display state seen: after RTE 2 LEGS was viewed, that CDU's LEGS key opened RTE 2 LEGS and its VNAV pages showed
route 2 (titles `CLB 1/3`, `CRZ 2/3`, `DES 3/3`, without ACT ECON, and the PROGRESS title without the flight number)
until RTE 1 LEGS was selected again (L6 on RTE 2 LEGS); nothing was modified. The first officer's CDU was left on
route 1, PROGRESS 1/3, scratchpad empty. The captain's CDU was never pressed (it moved to ACT ECON CRZ 2/3 with FL360
during the tour: the pilot's own changes).

## CLR on an empty scratchpad

One CLR (held about 0.3 s) on the first officer's empty scratchpad on PROGRESS 1/3: nothing changed (page and
scratchpad as before). So a second long CLR after one that already cleared the scratchpad does nothing, and Alt+C
can send two long CLRs (plan 08 measured that one long CLR clears the whole scratchpad 45 times in 60 and that a
second always clears what is left).
