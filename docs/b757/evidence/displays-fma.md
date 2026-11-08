# The FMA from memory: what plan 23 reads and gates on (plan 23, Task 1)

Measured 2026-11-08, 18:00 to 18:40 local, MSFS 2024 (1.9.24.0), the BlueBird 757 "BlueBird 757-200 RR N/W 4 Door"
in cruise at FL350 (504 kt ground speed) with CMD, LNAV, VNAV PTH and SPD, BBS Nest running. Memory was read only
(`PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ`, the private b757-memory probe scripts). Nothing was written to
the simulator: every question the plan put to a switch or knob was answered from the draw's code, the cockpit model
or the 2026-11-06 probe, as below. MSFSBA stayed running (the owner's test copy, never closed unasked): the probe
does not touch it, and the L:vars were read through the `VCockpit15` Coherent view, not the `BBSTablet` view the app
holds ([DBG-9]). Function numbers are WAT function indices of this build; offsets are from the object's start.

## Step 1: the build

- `BBS752.wasm`: 29,765,919 bytes, dated 2026-10-31 12:39:12, SHA-256
  `357292c941c563fd9d66878a96210ed16917e086547a45a2bf60f8d2d45efc3d`, package 0.1.0: the build the 2026-11-06 probe
  measured. No new BlueBird build.
- The published generator-3 layout (`bbs757/39462c4a…-69911a5bb8c57b24.json`, code hash `39462c4a…`) fits it: the
  owner's running copy's layout store chose it at 13:58:45 and 14:25:56 (`bbs757-module.log`: "using …"), after
  skipping the two older files, and found the anchor (0x181e4) in one allocation. The store's own fit check
  ([WASM-26]) is the check; no separate harness was run.
- Its `fma` reader: manager 0xf1334 span 256, pfd vptr 82028, pfd->afds 612, pfd->tmc 616, pitch 11924, pitch armed
  11944, roll 11932, roll armed 11940, A/T tmc+444, the five word tables (raw values; pitch and roll keyed value − 1
  in the draw, value in the tables). Every number below agrees with it.

## Step 2: the AFDS status column

The FMA draw is function 25856, the only function referencing UTF-32 "THR HLD" (one reference, at 490884). Its
status getter is function 30535 (one load, `afds+11948`). The draw:

| Condition (in this order) | Drawn |
|---|---|
| `afds+11948 == 1` (`i32.eq` with 1) | "CMD" |
| else this side's F/D byte is on (function 30471, Step 3) | "FD" |
| else | nothing |

- No other status value has a word: the draw tests `== 1` only and falls to the F/D test for every other value, so
  a status value that is not 1 is never "unrecognised"; the 2026-11-06 probe's 2 ("seen with FD only") draws "FD"
  through the F/D byte, not by its own word.
- **The strip's gate:** the draw computes "this side's F/D on, or status == 1" once (frame byte 537) and draws the
  pitch column with pitch armed (lines 635-1310 of the function) and the roll column with roll armed (1415-2327) only
  while it holds. The A/T column (tmc+444) is drawn always. So with the Captain's F/D off and no CMD, the ADI shows
  only the A/T column.
- Live: status 1 with CMD engaged ("CMD"). The 2026-11-06 probe: cold and dark 0 (all blank); READY FOR TAXI with
  both F/Ds on "FD", status 2.

## Step 3: the F/D bytes

- One getter, function 30471 `(this, side)`: side 0 loads `i32.load8_u offset=11957`, any other side
  `offset=11958`; the result is `& 1`. (The plan expected two getters; it is one getter with two loads, side 0 first.)
- The draw calls it with side 0 when the PFD's side field (`pfd+88`, getter 8585) is 2, else with 1. Live: the
  Captain's PFD (manager+16, 0x2e9430) has side 2, the First Officer's (manager+24) side 3. So the Captain's PFD reads
  +11957, the First Officer's +11958.
- The variables: the cockpit model (`BBS_752_BASE_INTERIOR.xml`) gives the Captain's F/D switch
  (`B757_Captain_FD`, node `B757_MCP_SW_FD_Cap`) `(L:BBS_FD1_SWITCH) ! (>L:BBS_FD1_SWITCH)` and the First Officer's
  (`B757_FO_FD`) `L:BBS_FD2_SWITCH`. The 2026-11-06 probe switched the Captain's F/D off and read +11957 0 and +11958 1
  at once. **+11957 follows `L:BBS_FD1_SWITCH`, +11958 `L:BBS_FD2_SWITCH`.**
- Live: `L:BBS_FD1_SWITCH` 1, `L:BBS_FD2_SWITCH` 1, both bytes 1. The node variables `L:B757_MCP_SW_FD_Cap` and
  `_FO` read 0 with both F/Ds on: they are not the switch state; never gate on them.

## Step 4: the MCP's selected altitude and heading

- The `mcp` object (class `N3bbs3mcpE`, vptr 117616) is the one in static slot 0xf1048 (live 0x2c26c0). Pointers to
  it: `afds+11848`, `pfd+608` (beside pfd->afds 612 and pfd->tmc 616), `pfd+1392`, `pfd+2444`, `pfd+3772`,
  `tmc+2868`.
- **The route: `afds+11848`.** afds's own code reads the MCP through it: function 30499 (an afds virtual) does
  `i32.load offset=11848` then calls function 22147 (one load, `f64.load offset=856`); function 30469 (called from
  the afds virtual 30466) does `i32.load offset=11848` then calls function 30481 (one load, `f64.load offset=848`).
  22147 is also called by the CDU's VNAV page (`N3bbs13cdu_vnav_pageE`) and 18 other functions.
- **The fields (f64):**

  | Field | Offset | Live | Variable, live |
  |---|---|---|---|
  | Selected heading | mcp+848 | 65.0 | `L:BBS_AP_TARGET_HEADING` 65 |
  | Selected altitude | mcp+856 | 35000.0 | `L:BBS_AP_TARGET_ALTITUDE` 35000 |
  | (Selected vertical speed, not used) | mcp+864 | 2400.0 | `L:BBS_AP_TARGET_VS` 2400 |

  Degrees and feet, whole numbers, the same units as the variables. In the object's first 4 KiB, 65 appears only at
  +848 (as f64, i32 or f32) and 35000 only at +856. Not changed once each (no simulator write was made); the gate's
  first live run (Task 11) and the owner's flight check them while they change.

## Step 5: the armed columns

From the draw, no write needed: at its start, getter 25868 (`afds+11940`) is stored in frame slot 524 and getter
25869 (`afds+11944`) in slot 520. The if-chain that draws G/S (9), VNAV (5) and FLARE (10) compares slot 520; the one
that draws LNAV (5), LOC (3), B/CRS (6) and ROLLOUT (4) compares slot 524. **Pitch armed is `afds+11944`, roll armed
`afds+11940`, as the layout has them**; the 2026-11-06 probe's table had them the other way. Live: both 0 (LNAV and
VNAV PTH active, nothing armed).

## Step 6: the waiting signal

The package's ten titles (`aircraft.cfg`), every one containing "757":

- BlueBird 757-200 PW, PW AWE, PW N/W, PW N/W AWE, PW NO CABIN
- BlueBird 757-200 RR, RR AWE, RR N/W, RR N/W 4 Door (loaded, read live as `TITLE`), RR Scimitar/W

No other aircraft was loaded (no flight is loaded unasked). The monitor waits while the title lacks "757"
(case-insensitive), as the plan says: no better signal exists (SimConnectManager has no flight-loaded state; the
757's memory is found only once the module is loaded).

## For the recipe (Task 5)

- Status: the getter called right before U"CMD" in the draw, compared `i32.eq` with a constant (1) whose branch
  draws U"CMD"; the other branch's word U"FD" is the F/D word.
- F/D: the getter the draw calls with `i32.const 0` and `i32.const 1` before U"FD"; its first `i32.load8_u` (side 0)
  is the Captain's, its second the First Officer's.
- MCP: `afds+11848`, and the two one-load `f64` getters afds's code calls through it.
