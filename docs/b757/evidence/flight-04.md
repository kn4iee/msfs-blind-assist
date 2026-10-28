# Plan 04 measurements: COM transfer, VOR courses on the CDU, the ILS panel (spec-gaps 3.1, 3.2; R54, R75)

Measured 2026-10-26 on the ground at KATL (BlueBird 757-200 RR, BBS Nest running, MSFSBA closed) by a Claude agent
with the owner's go-ahead, through the simconnect MCP and the Coherent debugger (`docs/b757/tools/live/`). The owner
stated that the flight was in its starting panel state when the measurements began (only the batteries on and the
APU started), so the ILS panel's load state is PARK with fraction .10 (section C proves the .10 by stepping). The
raw captures (snapshots, window captures, montages, scratch scripts) were not committed.

Setup: MSFS 2024, gate B1 (CDU LAST POS N33°38.3 W084°26.2), engines off, parking brake set, measured 11:26-11:51 PC
time (EDT). No route in the FMC; the captain's CDU on IDENT, POS INIT REF AIRPORT `----`.

## Method

- Coherent views this session: `VCockpit13` = BBSTablet (snapshots, the CDU agent, L:var writes from JS), `VCockpit14`
  = the main displays (captain ADI `WasmGaugeLiveView_24` at 21,8; captain EHSI `_18` at 21,735; EICAS `_21`/`_23`;
  EFB `_25`), `VCockpit15` = 18 small panel gauges (`_0`-`_17`; the ILS panel is `_12`, see C).
- `cdu.sh` first answered `noagent`: the repo's `MSFSBlindAssist/Resources/coherent-bbs757-tablet-agent.js` was
  installed into the BBSTablet view with `coherent-eval.ps1 -ExprFile` (answer `MSFSBA_BBS757_TABLET_INSTALLED`). It
  stays installed (the app re-installs over it; a listener is never unregistered by design). CDU 2's screen had no
  frame until its first key, so CDU 2's starting page is unknown.
- Noise: the repo `noise.txt` plus six vars that drifted in idle snapshots this session (`B757_M730_BATT_PERCENT`,
  `B757_RR_START_N1_L`, `B757_RR_START_N1_R`, `B757_MASI_NEEDLE_CA`, `B757_MASI_NEEDLE_FO`, `B757_SAI_NEEDLE`).
- Held buttons were pressed with a scratch copy of `press.sh` (JS `SetSimVarValue` 1, `setTimeout` 200 ms, 0, then a
  snapshot diff 2 s later): the 200 ms hold is the page's own timer, not MCP round trips.
- **MobiFlight drops an identical repeated calculator string.** The second identical `(>K:COM_STBY_RADIO_SWAP)`
  (11:31:24) did nothing (a Coherent read confirmed); `0 (>K:COM_STBY_RADIO_SWAP)` then worked. Every later MCP
  write was made unique (`n 0 * …`). The app's `ExecuteCalculatorCodeUnique` (`BuildUniqueCalcCode`:
  `"{seq} 0 * {rpn}"`) already covers this; any code path that sends the same RPN twice without it loses the second.

## A. COM transfer (Task 4 step 1)

Start (11:27): COM1 121.900 / 120.900, COM2 121.500 / 130.800, COM3 124.850 / 124.850 (active / standby);
`BBS_RTP_L_LIGHTS` 2, `BBS_RTP_C_LIGHTS` 4, `BBS_RTP_R_LIGHTS` 8; `B757_PED_COMHEAD_1/2/3_SWAP_LIGHT` 0.

| Time | Code | COM1 | COM2 | COM3 |
|---|---|---|---|---|
| 11:28:20 | `127300000 (>K:COM3_STBY_RADIO_SET_HZ)` | 121.900/120.900 | 121.500/130.800 | 124.850/**127.300** |
| 11:28:34 | `B757_PED_COMHEAD_2_SWAP_BUTTON` 1, 200 ms, 0 | – | **130.800/121.500** | – |
| 11:28:52 | same again | – | 121.500/130.800 (back) | – |
| 11:29:06 | `B757_PED_COMHEAD_3_SWAP_BUTTON` 1, 200 ms, 0 | – | – | **127.300/124.850** |
| 11:29:19 | same again | – | – | 124.850/127.300 (back) |
| ~11:30:58 | `(>K:COM_STBY_RADIO_SWAP)` | **120.900/121.900** (held 15 s) | – | – |
| 11:31:24 | identical string again | unchanged (dropped, see Method) | – | – |
| 11:31:51 | `0 (>K:COM_STBY_RADIO_SWAP)` | 121.900/120.900 (back) | – | – |
| 11:32:05 | `1 0 * (>K:COM2_RADIO_SWAP)` | – | **130.800/121.500** (held 4.5 s) | – |
| 11:32:19 | `2 0 * (>K:COM2_RADIO_SWAP)` | – | 121.500/130.800 (back) | – |
| 11:32:27 | `3 0 * (>K:COM3_RADIO_SWAP)` | – | – | **127.300/124.850** (held 4.5 s) |
| 11:32:39 | `4 0 * (>K:COM3_RADIO_SWAP)` | – | – | 124.850/127.300 (back) |
| 11:32:48 | `124850000 (>K:COM3_STBY_RADIO_SET_HZ)` | – | – | 124.850/124.850 |

Each read was taken about 1.5 s after the write (stock vars through the MCP and through `SimVar.GetSimVarValue` in
the BlueBird view; they agreed). No L:var outside noise changed during any of the four panel presses (the SWAP_LIGHTs
are 0 before and after; a snapshot 2 s later cannot see a momentary light). Final (11:49 and 11:51): all six as at
start, RTP lights 2/4/8; full snapshot s0 to the end of A: no L:var change.

Verdict A:
- The three stock swap events all work on BlueBird and hold: `COM_STBY_RADIO_SWAP` swaps COM 1, `COM2_RADIO_SWAP`
  COM 2, `COM3_RADIO_SWAP` COM 3. R54's "stock swap first" succeeds for every radio on the ground.
- Panel 2's transfer swapped COM 2 (VHF C) and panel 3's COM 3 (VHF R). By "a panel's button swaps the radio its
  lights select", **panel 2 = `BBS_RTP_C_LIGHTS` (4) and panel 3 = `BBS_RTP_R_LIGHTS` (8): the reverse of the
  belief** (panel 2 = R, panel 3 = C). Caveat: every panel currently selects the radio of its own number (1 to
  VHF L, 2 to VHF C, 3 to VHF R), so this cannot tell "follows the panel's selection" from "panel n always swaps COM
  n"; separating them needs a panel's VHF select button (`B757_PED_NAV_n_VHFx_BUTTON`), which the brief did not list,
  so it was not pressed.

## B. VOR courses on the CDU, and the way to APPROACH REF (Task 5 step 1)

Start: `BBS_VOR_L_COURSE` 0, `BBS_VOR_R_COURSE` 0, `BBS_ILS_COURSE` 0; `B757_MCP_VOR1_MODE_AUTO` 1,
`B757_MCP_VOR2_MODE_AUTO` 1; CDU 1 on IDENT (never pressed; still IDENT at the end, scratchpad empty); CDU 2 unknown
(no frame before the first key).

CDU 2 NAV RADIO after `NAVRAD` (11:33:24), rows 0-13:
```
00|        NAV RADIO       |
01|VOR L              VOR R|
02|116.90AGSU        ------|
03|CRS      RADIAL      CRS|
04|                        |
...
11|        PRESELECT       |
12|------            ------|
13|                        |
```
In AUTO the CRS line (row 4) and RADIAL are blank.

| Time | CDU 2 keys | Result |
|---|---|---|
| 11:33:33 | `NUM0 NUM9 NUM5` | scratchpad `095` |
| 11:33:45 | `L2` (CRS under VOR L, AUTO) | nothing: no new screen frame, no message, scratchpad keeps `095`, `BBS_VOR_L_COURSE` 0 |
| 11:34:08 | `R2` (VOR R `------`) | nothing, same |
| 11:34:33 | `KEYCLR` x3 | one CLR (250 ms hold) deletes one character |
| 11:34:52 | `116.90` + `L1` (manual tune of the same station) | row 2 `116.90RGSU`, row 4 `000` (CRS appears); scratchpad cleared by the entry; no L:var change (VOR1 AUTO stays 1) |
| 11:35:10 | `095` + `L2` | row 4 `095`; **`BBS_VOR_L_COURSE` 0 to 95**; no message; **scratchpad still shows `095`** after the entry |
| 11:35:39 | `R2` (VOR R still `------`) | nothing |
| 11:36:08 | CLR x3, `116.90` + `R1` | accepted this time: row 2 right `GSUR116.90`, row 4 right `000`; `BBS_RDMI_L/R_WIDE_BRG_POINTER` 0 to 5.385 (both RDMIs' wide pointer = VOR R bearing) |
| 11:36:27 | `095` + `R2` | row 4 right `095`; **`BBS_VOR_R_COURSE` 0 to 95**; scratchpad keeps `095` |
| 11:36:44 | CLR x3, `000` + `L2` | `BBS_VOR_L_COURSE` 95 to 0, row 4 left `000` |
| 11:37:01 | MCP `0 (>L:BBS_VOR_R_COURSE)` | var 95 to 0 and **the CDU's CRS R followed (`000`)**: the variable is two-way |
| 11:37:13 | CLR x3, `KEYDEL` + `L1` | VOR L back to `116.90AGSU`, its CRS blank |
| 11:37:29 | `KEYDEL` + `R1` | VOR R back to `------`, its CRS blank; page as at 11:33:24 |
| ~11:37:58 | MCP `0 (>L:BBS_RDMI_L_WIDE_BRG_POINTER) 0 (>L:BBS_RDMI_R_WIDE_BRG_POINTER)` | the wide pointers had latched 5.385 after VOR R lost its station; written back to 0, held (11:38:00 and 11:38:05 snapshots = start) |

No CDU message appeared on either CDU at any point. Session 8 saw VOR R refuse a frequency; today it took one.

Verdict B (VOR courses):
- **The CDU sets a VOR course only while that VOR is manually tuned** (row 2 `R`): the CRS field then shows `000`
  and takes `095` + LSK 2L / 2R, which writes `BBS_VOR_L_COURSE` / `BBS_VOR_R_COURSE`. **In AUTO (`A`) the CRS
  field is blank and an entry is silently ignored**: no message, scratchpad kept, no frame.
- BlueBird keeps the scratchpad after a CRS entry (a frequency entry into 1L / 1R clears it). An app that types
  courses must clear the scratchpad itself afterwards, or the next entry appends to it.
- A direct calculator write of `BBS_VOR_R_COURSE` shows on the CDU's CRS line (in manual tuning). Whether a direct
  write holds, or matters, while the VOR is in AUTO was not measured.
- For the plan: the pilot can set a VOR course from the CDU window only after tuning that VOR by hand; a VOR course
  for an auto-tuned VOR has no CDU path. Whether Ctrl+N keeps VOR course boxes is the controller's / owner's call.

APPROACH REF path on CDU 2 (11:38):

| Key | Page title (row 0) | Notes |
|---|---|---|
| `INIT` | `      POS INIT       1/4` | rows: LAST POS N33°38.3 W084°26.2, REF AIRPORT `----`, GATE, UTC(GPS) 1538Z, SET IRS POS, `<INDEX            ROUTE>` |
| `L6` (`<INDEX`) | `     INIT/REF INDEX     ` | `<IDENT` 1L, `NAV DATA>` 1R, `<POS` 2L, `ALTN>` 2R, `<PERF` 3L, `<TAKEOFF` 4L, `<APPROACH` 5L, `MAINT>` 6R |
| `L5` (`<APPROACH`) | `      APPROACH REF      ` | below |
| `NAVRAD` | `        NAV RADIO       ` | CDU 2 left here (11:38:37 and at the end) |

APPROACH REF (no route), all 14 rows:
```
00|      APPROACH REF      |
01| GROSS WT               |
02|???.?                   |
03|                        |
04|                        |
05|                        |
06|                        |
07|              FLAP/SPEED|
08|                  --/---|
09|                        |
10|                        |
11|------------------------|
12|<INDEX                  |
13|                        |
```
No ILS line without an arrival runway (session 8, with a route, showed `ILS 09L 110.50 IHZK`, `FRONT CRS 095°`).
Way in from any page on the ground: `INIT`, `L6` (INDEX; from POS INIT it is 6L), `L5`. INIT REF gave POS INIT
here; whether `INIT` alone gives APPROACH REF in descent (Boeing's phase-dependent INIT REF) is unmeasured.
APPROACH REF's own `<INDEX` is 6L.

## C. ILS panel on the ground (Task 6 step 1, ground parts)

Start: ILS display `-----` (PARK) with course `000`, hidden fraction .10 (proven by the first step: PARK -1 to
111.10); `BBS_ILS_COURSE` 0; `BBS_LOC_DEV_DEG` 0; captain ND mode `B757_ADI_1_CTR_KNOB` 2 (MAP).

### Readable source

- **Variables: none.** One `-1 (>L:B757_ILS_FREQ_WHOLE_INC)` (MCP, 11:40:37): full snapshots before (11:40:29) and
  after (11:40:42) differ in nothing outside noise; the request var reads 0 again within 0.6 s on every step.
  `NAV ACTIVE FREQUENCY:1..4` stayed 110.50 and `NAV HAS LOCALIZER:1..3` 0 with 108.70 tuned (stock NAV unused, as
  in session 8).
- **The ILS panel's display is the gauge `WasmGaugeLiveView_12` in `VCockpit15`** (668 x 93 at 400,852): a frequency
  window and a course window (`----- 000` parked, `111.10 000` tuned). Overlaying that live-view image in
  `VCockpit14` over a visible display (a scratch variant of `nd-mirror-on.js`: an `<img src="WasmGaugeLiveView_12">`
  over the upper EICAS rect 747,8 859x704; cross-view `src` works) and a `capture.ps1` window capture shows it,
  readable after a 2-3x crop. `canvas.toDataURL` of a live view returns an empty string, so only a window capture
  reads it. Other `VCockpit15` gauges seen: `_4` transponder (`ATC L 1200`), `_5`/`_6` ADF (`0190.0`), `_13`/`_14`
  `0012 ----`, `_15`/`_16` `200`, `_0` `36 35 35`, `_17` fuel quantity panel.
- **Captain's EHSI in APP mode** (`1 (>L:B757_ADI_1_CTR_KNOB)` 11:45:58, back with `2 (…)` ~11:48:28), magnified
  with a scratch overlay of `WasmGaugeLiveView_18`: bottom-left `ILS` (green), bottom-right green dashes `------`,
  top-left `DME ---`, top-right `------Z`; no frequency, no ident, no deviation pointer, with 108.75 tuned and with
  108.70 tuned (KATL RWY 26L IBRU per the app's fs2024 navdata; KATL also has 108.50 27L, 108.90 09R, 109.30 08L,
  109.90 08R, 110.10 26R, 110.50 09L, 111.30 27R, 111.55 10, 111.75 28). The EHSI does not show the tuned ILS
  frequency.
- **Manuals** (Complete Flight Guide v1.01, printed page numbers): nothing on how PARK is set or left. p. 43 and
  p. 106 "ILS: SET OR PARKED AS REQUIRED — Tune the required ILS, or park it when not required"; p. 61 After Landing
  "ILS CONTROLS … PARK AS APPROPRIATE"; p. 168 consider parking the ILS after takeoff when its course or DME is no
  longer needed; p. 192 parking the ILS while approach guidance is selected "can trigger an RW/ILS CRS ERROR or
  RW/ILS FREQ ERROR scratchpad alert"; p. 201 After Landing checklist "ILS Control Panel DESELECTED"; p. 97 the
  standby attitude indicator's small bottom-left knob is its ILS selector; p. 102 the EFIS mode selector chooses
  MAP, PLAN, VOR or ILS; p. 94 "AUTO radio tuning is available with the EFIS in MAP or PLAN". The User Procedures
  guide repeats pp. 168, 192 and 201.

### Knob behaviour

Read from the gauge after every step; steps written as `SetSimVarValue` in the BBSTablet view, 2.7 s apart.

| Steps | Displays |
|---|---|
| WHOLE -1 x20 from PARK (11:40:37-11:42:46) | 111.10, 110.10, 109.10, 108.10, `-----`, then the same five again, four times in all; ends at PARK |
| WHOLE +1 x6 (11:43:00) | 108.10, 109.10, 110.10, 111.10, `-----`, 108.10 |
| FRAC +1 x20 from 108.10 (11:43:25) | .15 .30 .35 .50 .55 .70 .75 .90 .95 **.10** .15 … .95 .10 (all 108.xx: no carry) |
| FRAC -1 x3 (11:44:41) | 108.95, 108.90, 108.75 |
| WHOLE +1 x4 (11:44:50) | 109.75, 110.75, 111.75, `-----` |
| FRAC +1 at PARK (11:45:15) | `-----` |
| WHOLE +1 (11:45:27) | **108.75** |
| FRAC -1 (11:47:42) | 108.70 |
| restore: FRAC +1 x4, WHOLE -1, then -1 and +1 to prove it (11:48:39-57) | 108.75, 108.90, 108.95, 108.10, `-----`, 111.10, `-----` |

Verdict C (knobs):
- **The whole knob is a ring with PARK in it: 108 to 109 to 110 to 111 to PARK to 108 (+1), the reverse for -1.** It
  never stops at an end. Leaving PARK: +1 gives 108.xx, -1 gives 111.xx.
- **The fraction knob steps through the ILS channels only: .10 .15 .30 .35 .50 .55 .70 .75 .90 .95** (0.05 then
  0.15 alternately), wrapping .95 to .10 inside the same MHz with no carry. Range 108.10-111.95, 40 channels, plus
  PARK.
- The whole knob keeps the fraction (108.75 to 109.75). **PARK remembers the fraction** (111.75, PARK, 108.75) and
  **the fraction knob does nothing while parked** (FRAC +1 at PARK, then WHOLE +1 still gave 108.75).
- So a target f can be reached from a known state in at most 2 whole + 5 fraction steps, but the app cannot read the
  state back: only a window capture of `WasmGaugeLiveView_12` shows it.
- The course window showed `000` throughout, with `BBS_ILS_COURSE` 0 (not written today, so "the window shows
  `BBS_ILS_COURSE`" is an inference).

### Unexpected changes (item stopped, put back, written down)

1. **`BBS_LOC_DEV_DEG` 0 to -18.498** somewhere between 11:40:42 and 11:45:50, while the stepping passed KATL ILS
   frequencies (110.10, 108.50, 108.70, 108.90); it stayed at about -18.498 with the ILS parked (latched, like the
   RDMI pointers). Which station set it was not pinned down: the item was stopped. Written back with MCP
   `0 (>L:BBS_LOC_DEV_DEG)` at ~11:51:03; it held 0 at 11:51:07 and 11:51:12. **A candidate readable "localizer
   received" signal** for the in-flight work (it is a latched value, so it cannot by itself say what is tuned now).
2. **`B757_MCP_VOR1_MODE_AUTO` 1 to 0** (VOR/DME panel `117.40 MAN`, CDU NAV RADIO VOR L `117.40M`, CRS `000`)
   between 11:47:48 and 11:48:32. The only write in that window was the captain's ND mode, put back from APP (1) to
   MAP (2) with a direct `2 (>L:B757_ADI_1_CTR_KNOB)` at ~11:48:28 (it had stayed AUTO for 2.5 min in APP).
   Plausibly BlueBird's auto-tuning logic around EFIS modes (manual p. 94: AUTO tuning only in MAP or PLAN), but not
   reproduced. Put back with the VOR/DME 1 button (`B757_MCP_VORDME_1_BUTTON` 1, 200 ms, 0) at 11:50:52 to
   `VOR1_MODE_AUTO` 1, panel `116.90 AUTO`, CDU `116.90AGSU`. VOR L's hidden manual frequency is now 117.40; its
   start value was hidden behind AUTO and is unknown. Warning for any app code that writes the EFIS mode knob var
   directly: check VOR AUTO afterwards.

### C step 3 (RW/ILS FREQ ERROR / CRS ERROR): skipped

No route and no arrival runway: APPROACH REF shows no ILS line. `BBS_ILS_COURSE` was not written. Both CDUs'
scratchpads were empty at 11:49:18 (CDU 1 IDENT, CDU 2 NAV RADIO).

### C step 4: not measured (the owner's, in flight)

- Does RW/ILS FREQ ERROR or RW/ILS CRS ERROR appear only in descent/approach, on which CDU, and how long after the
  mismatch?
- Does the localizer capture (LOC/APP) with the ILS parked, or with a wrong frequency?
- Does `BBS_LOC_DEV_DEG` (or anything else) follow a received localizer in flight, and clear when the ILS is parked?
- Does the EHSI's bottom-right field show the ILS frequency or ident when a localizer is actually received?
- Does `INIT` alone open APPROACH REF in descent?

## Every value changed, and its restored value

| Value | Start | Changed to | Restored | Confirmed |
|---|---|---|---|---|
| COM3 standby | 124.850 | 127.300 | 124.850 (`124850000 (>K:COM3_STBY_RADIO_SET_HZ)`) | MCP + Coherent 11:32:50, 11:49, 11:51 |
| COM2 active/standby | 121.500 / 130.800 | swapped (panel 2 button, `COM2_RADIO_SWAP`) | swapped back | 11:28:52, 11:32:21 |
| COM3 active/standby | 124.850 / 124.850 (127.300 during A) | swapped (panel 3 button, `COM3_RADIO_SWAP`) | swapped back | 11:29:19, 11:32:41 |
| COM1 active/standby | 121.900 / 120.900 | swapped (`COM_STBY_RADIO_SWAP`) | swapped back | 11:31:53 |
| CDU 2 VOR L tuning | `116.90AGSU` (AUTO) | `116.90RGSU` | `KEYDEL` + L1 to `116.90AGSU` | 11:37:13, 11:50:55 |
| CDU 2 VOR R tuning | `------` | `GSUR116.90` | `KEYDEL` + R1 to `------` | 11:37:29 |
| `BBS_VOR_L_COURSE` | 0 | 95 | 0 (`000` + L2) | snapshots 11:38, 11:51 |
| `BBS_VOR_R_COURSE` | 0 | 95 | 0 (direct write) | snapshots 11:38, 11:51 |
| `BBS_RDMI_L/R_WIDE_BRG_POINTER` | 0 | 5.385 (side effect of tuning VOR R) | 0 (direct write) | snapshots 11:38, 11:51 |
| CDU 2 page | unknown (no frame before the first key) | NAV RADIO, POS INIT, INIT/REF INDEX, APPROACH REF | NAV RADIO (as the brief asks) | 11:49:18 |
| CDU 2 scratchpad | empty | 095 / 000 | empty | 11:49:18 |
| ILS panel | PARK, fraction .10 | 111.10 … 108.70 | PARK, fraction .10 (proved by -1 to 111.10, +1 to PARK) | capture 11:48:57 |
| `B757_ADI_1_CTR_KNOB` (captain ND mode) | 2 MAP | 1 APP | 2 MAP | snapshot 11:48:32, 11:51 |
| `B757_MCP_VOR1_MODE_AUTO` | 1 AUTO | 0 MAN (unexpected, see C) | 1 (VOR/DME 1 button) | snapshot 11:51, panel capture 11:51 |
| `BBS_LOC_DEV_DEG` | 0 | -18.498 (unexpected, see C) | 0 (direct write) | snapshots 11:51:07, 11:51:12 |
| VCockpit14 overlay `msfsba_mirror2` | none | gauge mirrors | removed (`removed`); `nd-mirror-off.js` answered `none` | 11:49:18 |

Full snapshot at the start (11:27:05) against the end (11:51:07 and 11:51:12): no difference outside the session
noise list. Stock COM at the end: 121.900/120.900, 121.500/130.800, 124.850/124.850.

Left as is: the CDU tablet agent installed in the BBSTablet view; VOR L's hidden manual frequency 117.40 (start value
unknown); CDU 2 on NAV RADIO (its start page could not be read).

## Round 2: ILS read-back probes (12:07-12:19, stopped for the owner's flight programming)

Same aircraft and gate. Round-2 start (12:07:14): ILS PARK (fraction .10), `BBS_ILS_COURSE` 0, `BBS_LOC_DEV_DEG` 0,
captain ND mode MAP (`B757_ADI_1_CTR_KNOB` 2), VOR L AUTO, `B757_MAIN_M_Gau_STBY_GYRO_SELECTOR` 0.
**Changed between round 1's end (11:51:12) and 12:07:14 with no write by the agent:** `B757_FC_SPOILER_LEVER` 0 to
0.815 and `B757_PSU_STATE` 1 to 0 (`B757_SPOILER_LEVER_POS` stayed 0). Not touched; both are still so at the end of
the round.

Method: each step wrote the knob request (a `SetSimVarValue` in the BBSTablet view), waited 4-5 s, took a full
snapshot and a capture of the ILS gauge (overlaid on the upper EICAS, as in C), and diffed against the previous
snapshot. Every tuned value was confirmed on the gauge picture.

### D1. A live "localizer received" signal

Sweep 1, SADI ILS selector at its start value 0 (OFF). Only `BBS_LOC_DEV_DEG` moved; no other variable changed at any
step (no DME, ident, marker or flag variable):

| Tuned | KATL ILS | `BBS_LOC_DEV_DEG` |
|---|---|---|
| PARK, 108.10, 108.15, 108.30, 108.35 | none | 0 |
| 108.50 | 27L IFSQ (270°) | 0 |
| 108.70 | 26L IBRU (270°) | 0 |
| 108.90 | 09R IFUN (090°) | **-18.498** |
| 109.90 | 08R IATL (090°) | **18.227** |
| 109.95, 109.10, 109.15 | none | 18.227 (held) |
| 109.30 | 08L IHFW (090°) | **26.248** |
| 110.30, 110.15 | none | 26.248 (held) |
| 110.10 | 26R IGXZ (270°) | 26.248 (held: not received) |
| 110.50 | 09L IHZK (090°) | **-6.642** |
| 111.50, 111.70, 111.35 | none | -6.642 (held) |
| 111.55 | 10 IOMO (090°) | -6.642 (held: not received) |
| 111.75 / 111.30 | 28 IPKU / 27R IAFA (270°) | -6.642 (held) |
| PARK | | -6.642 (held) |

- At the gate only the four localizers that face the terminal (09R, 08R, 08L, 09L, front course 090°) are received;
  the 270° ones (antenna behind us) and RWY 10 are not.
- **`BBS_LOC_DEV_DEG` follows a received localizer one way only**: it takes a new value per runway (so it tells
  stations apart) but keeps the last value on a non-ILS channel, an unreceived station and PARK. It cannot say what is
  received now.

The SADI's ILS selector is `B757_MAIN_M_Gau_STBY_GYRO_SELECTOR` (control map "Standby attitude indicator / Gyro
Selector", positions 0-2; the knob face reads OFF, F/CRS, B/CRS; start 0). Set to 1 at 12:11:40:

| Time | Tuned | `B757_SADI_LOC` | `B757_MAIN_M_Gau_STBY_GYRO_knob` | `BBS_LOC_DEV_DEG` |
|---|---|---|---|---|
| 12:11:40 | PARK, selector 1 | 0 | 0 | -6.642 |
| 12:11:48 | 111.30 (27R) | 0 | 0 | -6.642 |
| 12:12:00 | 109.30 (08L) | **18.446** | **4.602** | 26.248 |
| 12:12:17 | 108.30 (none) | 0.001 | 0.0003 | 26.248 |
| 12:12:24 | 109.30 (08L) | 18.523 | 4.623 | 26.248 |
| 12:12:30 | 108.30 | 0.002 | 0.0004 | 26.248 |
| 12:12:36 | PARK | 0.0 | ~0 | 26.248 |
| 12:12:48 | 109.30 (08L) | 18.276 | 4.556 | 26.248 |
| 12:12:55 | 110.30 | 0.001 | 0.0004 | 26.248 |
| 12:13:36 | 110.50 (09L) | **49.996** | 4.750 | -6.642 |
| 12:13:43 | 110.50, selector **2** | 12.5 | 4.750 | -6.642 |
| 12:13:50 | 110.50, selector 1 | 50.0 | 4.750 | -6.642 |
| 12:13:57-12:14:25 | 109.50, 108.50 (27L), 108.55, 108.70 (26L), 108.75 | 0.0 | ~0 | -6.642 |
| 12:14:33 | 108.90 (09R) | **49.992** | 4.749 | -18.498 |
| 12:14:40 | 109.90 (08R) | **19.0** | 4.750 | 18.227 |
| 12:14:47 | 110.90 (none) | 0.0 | ~0 | 18.227 |

- **With the SADI ILS selector on (1), `B757_SADI_LOC` and `B757_MAIN_M_Gau_STBY_GYRO_knob` follow the tuning BOTH
  ways**: non-zero within 4 s of tuning a received localizer, back to about 0 (an animated decay: 0.001 after 4 s,
  ~1e-88 later) on a non-ILS channel, an unreceived ILS or PARK. With the selector at 0 both stay 0. This is the only
  live "localizer received now" signal found. Its cost: the standby ADI's ILS selector must be on (F/CRS), a cockpit
  switch the pilot sets.
- `B757_SADI_LOC_FLAG` and `B757_SADI_GS_FLAG` stayed 0 throughout, selector on or off: no use.
- The SADI values do not identify a station: 08L 18.3-18.5 and 08R 19.0, 09L and 09R both 50 (apparently a full-scale
  stop), selector 2 (B/CRS) 12.5 on 09L. The SADI picture with 08L showed a localizer bar left of centre.
  `BBS_LOC_DEV_DEG` (latched) is the one that differs per runway.
- So a read-back could pair them: SADI non-zero = a localizer is received now; `BBS_LOC_DEV_DEG` changed = which
  one, compared with the runway's expected deviation (see "Localizer geometry"). On the ground that only works for
  the localizers in coverage. In flight: unmeasured.

Put back: selector 0 at 12:15:25 (with an unreceived channel tuned); ILS 110.95 to 110.10 to 111.10 to PARK, proved by
-1 to 111.10 and +1 to PARK on the gauge (12:15:31-12:15:57); `BBS_LOC_DEV_DEG` written 0 with MCP
`5 0 * (>L:BBS_LOC_DEV_DEG)` at ~12:16:20 (held at 12:16:23 and 12:19). `B757_SADI_LOC` and the gyro knob value read
about 1e-252 at the end (decayed to zero; the module writes them every frame).

### D2, first part: BlueBird's ILS debug page and saved logs (mostly not run in round 2)

- Done: the camera was set to instrument view 0 to see the EFB through a magnified mirror (`CAMERA VIEW TYPE AND
  INDEX:0` 1 to 2 and `:1` 1 to 0; the calculator write of `:1` alone changed the variable but not the view; with
  `:0` = 2 it did). The agent cycled instrument views 1-7 to look for one that shows the tablet: none does (1 main panel,
  2-3 overhead, 4 wide forward panel with both CDUs, 5 captain's panel and CDU, 6-7 circuit breaker panels). With a
  whole-EFB overlay on the ADI and EHSI, **the EFB was on its Home screen** (Aircraft, Navigation, Systems,
  Checklist, Settings icons in one row at the top). No EFB tap was made and the EFB agent was not installed, so the
  EFB was still on Home.
- **Not run (stopped for the owner's flight programming):** opening Systems, ILS debug (with a KATL localizer tuned,
  then parked), LOGS, SAVE LOGS and reading `work\bbs-debug-saved\*.txt`. No file was looked for or copied. Round 3
  ran it (below).

### Round-2 state at the end (12:19)

| Value | Round-2 start | Changed to | Restored | Confirmed |
|---|---|---|---|---|
| ILS panel | PARK, fraction .10 | the D1 tunings | PARK, fraction .10 | gauge 12:15:50 (111.10) and 12:15:57 (PARK) |
| `BBS_LOC_DEV_DEG` | 0 | -18.498, 18.227, 26.248, -6.642 | 0 (direct write) | snapshots 12:16:23, 12:19 |
| `B757_MAIN_M_Gau_STBY_GYRO_SELECTOR` (SADI ILS) | 0 | 1, 2, 1 | 0 | snapshots 12:15:25, 12:19 |
| `B757_SADI_LOC`, `B757_MAIN_M_Gau_STBY_GYRO_knob` | 0 | up to 50 / 4.75 | ~1e-252 (decays on its own) | snapshot 12:19 |
| Camera `CAMERA VIEW TYPE AND INDEX:0/:1` | 1 / 1 | 2 / 0-7 | 1 / 1 | MCP read 12:19, window capture = the start view |
| VCockpit14 overlays (`msfsba_mirror2`, `msfsba_efbz`) | none | ILS gauge mirror, EFB zoom | removed (`removed`, `removed`); `nd-mirror-off.js` answered `none` | 12:19:13 |
| EFB page | Home | not changed (no tap) | Home | overlay picture |

Full snapshot round-2 start to end: no difference outside noise except the decaying SADI values above (and the
spoiler lever and PSU state that moved before the round began, still as found). Captain ND MAP
(`B757_ADI_1_CTR_KNOB` 2), VOR L and R AUTO (CDU 2 NAV RADIO `116.90AGSU`, VOR/DME panel `116.90 AUTO`),
`BBS_ILS_COURSE` 0, VOR courses 0, COM 121.900/120.900, 121.500/130.800, 124.850/124.850, CDU 1 IDENT, CDU 2 NAV
RADIO. No Coherent connection is held (each `coherent-eval.ps1` call closes its socket; no ESTABLISHED socket on
:19999 at 12:19). The CDU tablet agent from round 1 is still installed in the BBSTablet view (the same version as the
app's: MSFSBA keeps it and its listener on connect).

## Round 3: the owner's route, the FMC as a comparator, the EFB (12:24-12:39)

The owner programmed KATL to KMIA in MSFSBA (MSFSBA was closed again before the measurements). Start (12:24:54):
CDU 1 on `ACT RTE 1 LEGS 1/7` (the owner's programming; not pressed by the agent); CDU 2 on NAV RADIO
(`116.90AGSU`, VOR R `------`). Also changed by the owner since round 2's end (snapshot diff): IRS L/C/R switches 0
to 2 (NAV) with the ALIGN lights on at 12:24, both flight director switches on. **During E3 the alignment
completed** (ALIGN lights, ADF/VSI flags and yaw damper INOP lights went off, autoland lights 2 to 1): not the
agent's doing, kept.

### E1. What the owner programmed (read only on CDU 2, 12:26; nothing selected, no EXEC, CDU 1 not pressed)

RTE (`RTE` key):
```
00|  ACT RTE 1          1/3|
01| ORIGIN             DEST|
02|KATL                KMIA|
03| RUNWAY           FLT NO|
04|RW09L             BVI2GP|
05| REQUEST        CO ROUTE|
06|<SEND         ----------|
07|                        |
08|               RTE COPY>|
09|------------------------|
10|<PRINT             ALTN>|
11|                        |
12|<RTE 2        PERF INIT>|
13|                        |
```
LEGS (`LEGS` key; identical to CDU 1's page at 12:24):
```
00|  ACT RTE 1 LEGS     1/7|
01| 095°HDG                |
02|(INTC)        ---/------|
03| 115°      6NM          |
04|GRITZ         ---/------|
05| 185°      9NM          |
06|HYZMN         ---/------|
07| 199°     31NM          |
08|BANNG         ---/------|
09| 184°     40NM          |
10|LUCKK         ---/------|
11|------------------------|
12|<RTE 2 LEGS    RTE DATA>|
13|                        |
```
DEP/ARR: on the ground the key opens `KATL DEPARTURES 1/4` directly: **SID BANNG3 `<ACT>`, runway 09L `<ACT>`**
(rows `BANNG3<ACT>          08R`, `CUTTN2       <ACT>   09L`: a SID's `<ACT>` follows its name, a runway's sits left
of it). `<INDEX` (6L) gives `DEP/ARR INDEX` (`<DEP KATL ARR>` row 2, `KMIA ARR>` row 4, RTE 1 (ACT)); `KMIA ARR>` (2R)
gives `KMIA ARRIVALS 1/6`: **STAR FROGZ5 `<ACT>`, approach ILS 09 `<ACT>`** (`CSTAL3     <ACT>  ILS 09`,
`FROGZ5<ACT>       ILS 27`; listed approaches ILS 08R, 09, 12, 26L, 27 on page 1). A STAR transition, if any, is on
a later page (not paged). PERF INIT is not filled (gross weight, ZFW, cruise altitude, cost index boxes). The route is
KATL 09L, BANNG3, then FROGZ5 and ILS 09 at KMIA, flight BVI2GP.

### E2. APPROACH REF with a route (CDU 2, 12:25)

`INIT` now gives **PERF INIT** (title `        PERF INIT       `; GR WT and ZFW boxes, FUEL `19.7 CALC`, CRZ ALT and
COST INDEX boxes, CRZ CG 25.0%, `<SEND` REQUEST at 5L, `<INDEX` 6L, `TAKEOFF>` 6R): PERF INIT is not filled. Then
`L6` gives `INIT/REF INDEX`, `L5` gives APPROACH REF. All 14 rows, exactly (24 columns between the bars; `☐` is
U+2610, the CDU's entry box, which the console printed as `?`; `°` is U+00B0):

```
00|      APPROACH REF      |
01| GROSS WT               |
02|☐☐☐.☐                   |
03|                        |
04|                        |
05|                        |
06|                        |
07| KATL09L      FLAP/SPEED|
08|12390FT3776M      --/---|
09| ILS 09L       FRONT CRS|
10|110.50 IHZK         095°|
11|------------------------|
12|<INDEX                  |
13|                        |
```

- **Row 9 is the ILS label line (` ILS 09L` left, `FRONT CRS` right); row 10 holds the frequency, ident and front
  course (`110.50 IHZK` from column 0, `095°` ending at column 23).** Row 7 holds the runway (` KATL09L`, small font,
  leading space) and row 8 its length (`12390FT3776M`). The FLAP/SPEED label is row 7 right, its value `--/---` row 8
  right (no VREF: GROSS WT is empty).
- **The runway shown is the ORIGIN's: KATL 09L**, not a KMIA runway, with the route programmed and an arrival selected
  (E1: STAR FROGZ5 and ILS 09 at KMIA are active). Session 8's `ILS 09L 110.50 IHZK` was the same (KATL departure runway). Whether APPROACH REF
  switches to the destination runway later (after takeoff or past some distance) is unmeasured on the ground.
- Colours: every cell `g`; label rows 1, 7, 9 and the dashes of row 11 are in the small font (cell attribute 1);
  rows 8 and 10 mix large and small cells.

### E3. The FMC as an ILS comparator, on the ground (12:27-12:31)

CDU 2 was on APPROACH REF (`ILS 09L` / `110.50 IHZK 095°`: the FMC's runway is the origin's KATL 09L, see E2), CDU 1
on ACT RTE 1 LEGS. Each read: snapshot, CDU 1 row 13, CDU 2 rows 9, 10, 13, gauge picture. Start: ILS PARK (gauge),
`BBS_ILS_COURSE` 0, `BBS_LOC_DEV_DEG` 0, SADI selector 0.

| Time | Action | Gauge | CDU 1 row 13 | CDU 2 row 13 | APPROACH REF rows 9-10 | `BBS_LOC_DEV_DEG` | SADI LOC / flags |
|---|---|---|---|---|---|---|---|
| 12:27:16 | start, parked | `----- 000` | empty | empty | `ILS 09L` / `110.50 IHZK 095°` | 0 | 0 / 0 0 |
| 12:27:59 | tuned 110.50 (W -1 x2, F +1 x4), +10 s | `110.50 000` | empty | empty | same | -6.642 | 0 / 0 0 |
| 12:28:17 | `95 (>L:BBS_ILS_COURSE)`, +10 s | `110.50 095` | empty | empty | same | -6.642 | 0 / 0 0 |
| 12:28:44, 12:28:59 | F +1 to 110.55, +10 s and +20 s | `110.55 095` | empty | empty | same | -6.642 | 0 / 0 0 |
| 12:29:20 | F -1 to 110.50, +10 s | | empty | empty | same | -6.642 | 0 / 0 0 |
| 12:29:37, 12:29:50 | `105 (>L:BBS_ILS_COURSE)`, +10 s and +20 s | | empty | empty | same | -6.642 | 0 / 0 0 |
| 12:30:10 | course back to 95, +10 s | | empty | empty | same | -6.642 | 0 / 0 0 |
| 12:30:26, 12:30:39 | PARK (W +1 x2), +10 s and +20 s | `----- 095` | empty | empty | same | -6.642 | 0 / 0 0 |

- **On the ground the FMC posts no RW/ILS FREQ ERROR or RW/ILS CRS ERROR**, on either CDU, for a wrong frequency
  (110.55), a wrong course (105), or PARK, with the route, runway and approach programmed. No CLR was needed. The
  manual ties the alert to approach guidance (p. 192), so whether and where it appears in flight stays the owner's
  question. APPROACH REF's ILS lines never changed with the tuning: they are the database's, not the radio's.
- **The ILS panel's course window shows `BBS_ILS_COURSE`** (`000` to `095` after the direct write): round 1's
  inference is now measured.
- **`BBS_LOC_DEV_DEG` does not depend on the selected course** (-6.642 with course 0, 95 and 105): it is the
  deviation from the localizer's own course. It latched -6.642 after PARK, as in round 2.
- No variable other than these (and the IRS alignment above) changed across E3.

End (12:31): ILS PARK with fraction .10, proved on the gauge (PARK +1 to 108.50, F -1 x4 to 108.35, .30, .15, .10,
W -1 to PARK, W -1 to 111.10, W +1 to PARK); `0 (>L:BBS_ILS_COURSE)` and `6 0 * (>L:BBS_LOC_DEV_DEG)` at 12:31:28,
both 0 at 12:31:33; both scratchpads empty; CDU 2 back on NAV RADIO (12:31:41); CDU 1 untouched on ACT RTE 1 LEGS.

### D2, continued: the EFB's Systems app (12:32-12:39)

- Before: the EFB's `work\` folder held only `NavigationData\`, `liveries\` and `efb_settings.json`; no
  `bbs-debug-saved\` folder. The EFB showed **Navigation, ACARS DASHBOARD** (the owner's KATL to KMIA OFP: RW 09L, SID
  BANNG3 LUCKK, flight 2GP / BVI2GP, 26 Oct 26, 02:30 UTC, initial altitude 37,000 ft, CI 30, AIRAC 2610).
- ILS tuned to 110.50 (KATL 09L, received: `BBS_LOC_DEV_DEG` -6.642), gauge `110.50 000`. Camera to instrument view 0
  (`:0` 2, `:1` 0), magnified EFB overlay.
- Taps (EFB-local, each answered `ok`): Home (19,62) to the home screen; **Systems (690,190)** to the `SYSTEMS` app on
  the ELECTRICAL diagram, title bar with `SELECT` at the right; **`SELECT` (1345,62)** to a drop-down with exactly
  four entries: `24 - ELECTRICAL`, `28 - FUEL`, `29 - HYDRAULICS`, `36 - PNEUMATICS`. `SELECT` again closed it. The
  top bar also showed `DEP -14:03`, a `START BOARDING` button and a red `4` badge (none touched).
- **Verdict D2: the ILS debug page and LOGS / SAVE LOGS are not offered in this build's Systems app** (the module's
  strings name them, `wasm-findings.md` section 6, but the menu lists only the four diagrams). No other way in was
  tried: no setting was changed (`efb_settings.json` has no debug-page key; its `show_debug_cursor` is true,
  unrelated). No log was saved; no `bbs-debug-saved` file exists.
- Put back: Home (19,62), then Navigation (503,190): the app reopened on **ACARS DASHBOARD**, the page it showed before
  (12:34:48). Overlays removed.

### Round 3 end state (12:39)

| Value | Round-3 start | Restored / now | Confirmed |
|---|---|---|---|
| ILS panel | PARK, fraction .10 | PARK, fraction .10 (D2 retune 110.50, F -1 x4 to 110.10, W +1 x2 to PARK; proved -1 to 111.10, +1 to PARK) | gauge, 12:38 |
| `BBS_ILS_COURSE` | 0 | 0 | snapshot 12:39 |
| `BBS_LOC_DEV_DEG` | 0 | 0 (`7 0 * (>L:BBS_LOC_DEV_DEG)`) | snapshot 12:39 |
| SADI ILS selector, ND mode, VOR L | 0, MAP, AUTO | 0, MAP (2), AUTO | snapshot 12:39 |
| CDU 2 | NAV RADIO | NAV RADIO, scratchpad empty | 12:39 |
| CDU 1 | ACT RTE 1 LEGS 1/7 | not pressed; ACT RTE 1 LEGS, scratchpad empty | 12:39 |
| The owner's route, runways, SID/STAR/approach | as programmed | kept (no key on any route page beyond page reads; no EXEC) | E1 |
| EFB | Navigation, ACARS DASHBOARD | same | 12:34:48 |
| Overlays | none | `msfsba_mirror2` removed, `msfsba_efbz` removed; `nd-mirror-off.js` answered `none` | 12:39:32 |
| **Camera** | type 1 index 1 (the default forward cockpit view; `CAMERA VIEW TYPE AND INDEX:0/:1` = 1/1, substate 1) | **NOT restored: instrument view 1** (type 2 index 1, substate 5, the main-panel view) | 12:38 |

**The camera could not be put back this time.** The type write that worked at the end of round 2 (JS
`SetSimVarValue('A:CAMERA VIEW TYPE AND INDEX:0', 1)` then `:1` = 1) was ignored from 12:35 on, as were the same
writes through the MCP calculator (`number` and `enum` units), `msfs_set_simvar` ("did not change"), writing type 0,
`CAMERA STATE` 2, `CAMERA SUBSTATE` 1, and the `VIEW_RESET` and `VIEW_PREVIOUS_TOGGLE` events. Index writes within
type 2 still work, so the agent left instrument view 1, the nearest to the start view. The owner's cockpit-view key
brings the default view back. Nothing on the aircraft depends on it, but any window-capture-based check (and the
app's AI display reads, if they assume a view) sees the instrument view until then.

Full snapshot round-3 start to end: only the IRS alignment the owner started (ALIGN lights, ADF/VSI flags, yaw damper
INOP, autoland lights) differs. COM 121.900/120.900, 121.500/130.800, 124.850/124.850. No Coherent socket open (0
ESTABLISHED on :19999). The EFB agent (`__MSFSBA_BBS757EFB`, installed by the tap script in the VCockpit14 view) and
the CDU tablet agent stay installed, as the app would leave them.

## Localizer geometry

A read-only check on 2026-10-26 (nothing written to the sim or the repo; the navdata database opened read-only): are
the four `BBS_LOC_DEV_DEG` values of D1 what the geometry of the KATL localizers predicts?

**Yes**, to within 0.15-0.26 degrees, and the ten KATL localizers are distinct enough at this position to tell apart.

- **The formula.** Predicted deviation = `wrap180((course + 180) - bearing(antenna to aircraft))`, with the course
  the database's `loc_heading` read as TRUE degrees and the bearing and distance the WGS84 geodesic from the localizer
  antenna to the aircraft. Positive = the aircraft is to the RIGHT of the inbound front course (for a 090 localizer:
  south of the centerline), negative = left (north). The sign fits all four received localizers. The aircraft's
  heading does not enter.
- **The aircraft** (read once from the sim): latitude 33.63753469792613, longitude -84.43528939668197, true heading
  300.1010315638472 (magnetic 305.5799, variation -5.4789), at a KATL gate between the 09L/27R runway and the 08R/26L
  runway, about 1.2 km past the 09 thresholds and 2.6 km short of the 27R threshold.
- **The database.** `DatabasePathResolver.ResolveExistingDatabasePath` gave
  `%APPDATA%\MSFSBlindAssist\databases\fs2024.sqlite` (the canonical path, no FBWBA copy); `fs2020.sqlite` is also
  there (built 2026-05-20; fs2024 2026-10-18). Which one the app opens depends on the sim version it detects. Table
  `ils` (Little Navmap schema, the one `LittleNavMapProvider.GetILSForRunway` reads): `ident`, `name`, `frequency`
  (kHz), `loc_heading` (degrees TRUE), `lonx` / `laty` (the localizer antenna), `mag_var`, `loc_airport_ident`,
  `loc_runway_name`.

Predicted against measured, the four received (fs2024 database; "measured" is the value the check was given, which is
D1's -18.498 for 09R and D1's other three rounded to one decimal):

| Rwy | MHz | Ident | Course (true) | Distance | Predicted | Measured | Measured minus predicted |
|---|---|---|---|---|---|---|---|
| 09R | 108.90 | IFUN | 89.97 | 1.10 NM | -18.24 | -18.498 | -0.26 |
| 08R | 109.90 | IATL | 89.97 | 1.76 NM | +18.35 | +18.2 | -0.15 |
| 08L | 109.30 | IHFW | 89.97 | 1.62 NM | +26.39 | +26.2 | -0.19 |
| 09L | 110.50 | IHZK | 89.99 | 1.52 NM | -6.44 | -6.6 | -0.16 |

With D1's three-decimal values for those three (18.227, 26.248, -6.642) the residuals come to about -0.12, -0.14 and
-0.20, so the 0.15-0.26 range is the check's own, good to about 0.05.

- **A near-constant offset of about -0.2 degrees**, on both sides of the course line. Cause not established (a
  nav-antenna offset from the aircraft datum, or the sim's antenna and course differing slightly from the database).
  Irrelevant at a 0.5 degree tolerance. The fs2020 database gives the same picture with a slightly worse fit (antenna
  positions differ by about 10 m, courses rounded to exactly 090.00 / 270.00; predicted +26.49, +18.43, -6.44, -18.26
  for 08L, 08R, 09L, 09R; residuals 0.16-0.29): the fs2024 database is the closer match, which suggests, but does not
  prove, that the sim is FS2024.
- **Distinctness.** Predicted deviations of all ten KATL localizers at the gate, sorted: -65.56 (26R), -59.45 (26L),
  -45.35 (10), -18.24 (09R), -6.44 (09L), +11.88 (27R), +18.35 (08R), +23.61 (27L), +26.39 (08L), +51.72 (28).
  Consecutive gaps: 6.1, 14.1, 27.1, 11.8, 18.3, 6.5, 5.3, **2.8**, 25.3 degrees. The closest pair (27L +23.6 against
  08L +26.4) is 2.8 degrees apart; the four that were actually received are at least 8.0 degrees apart. Against a
  residual of 0.15-0.26 degrees, a +-0.5 degree match window identifies a localizer uniquely here: a received
  localizer's `BBS_LOC_DEV_DEG` names the station.
- **The course is TRUE.** If `loc_heading` were read as magnetic (true = 89.97 - 5.55 = 84.42) the four predictions
  would be +20.84, +12.80, -11.99, -23.79 against +26.2, +18.2, -6.6, -18.5 measured: wrong by 5.5 degrees. The
  course is in the same frame as the database's `runway_end.heading`, which agrees with it to 0.02 degrees (Little
  Navmap stores both as true, from memory, not re-verified).
- **The 27L / 27R puzzle (open).** RWY 10 (45.4 degrees off the course line), 26L (59.5), 26R (65.6) and 28 (51.7) are
  outside the +-35 degree sector, so "not received" is what the standard model gives. But **27L (+23.6 degrees, 0.86 NM)
  and 27R (+11.9 degrees, 0.82 NM) are inside +-35 degrees and 17 NM and would be predicted received**, yet were not.
  The four received ones are 1.10-1.76 NM from their antennas; 27L and 27R are 0.82-0.86 NM. A sim-side minimum
  distance (about 1 NM) would fit, but that is a guess from four points. Either the sim's coverage is not the
  textbook sector or the 27L / 27R test needs repeating. For identification this costs nothing (a localizer that is
  not received gives no reading to match), but the app must not assume that "inside the textbook sector" means the sim
  receives it.
- **BlueBird's own navdata has the same KATL localizer frequencies** (checked afterwards, read only). BlueBird's work
  folder holds `work\NavigationData\db.s3db` (Navigraph cycle 2610, DFD v2), whose table `tbl_pi_localizers_glideslopes`
  has `llz_frequency`, `llz_latitude`, `llz_longitude`, `llz_truebearing`, `llz_identifier` and `runway_identifier`
  (`RWxx`). Its KATL frequencies are the same as the app's fs2024 data, so 27L and 27R not being received is no frequency
  mismatch. Predicted with BlueBird's own antenna positions, the differences from the measured values are -0.14 to
  -0.41 degrees. Every received localizer is 1.09 NM or more from its antenna, and 27L and 27R (inside the sector) are
  0.82-0.85 NM away, so BlueBird may ignore a localizer closer than about 1 NM; round 5 (G2.3) did not receive 27R at
  0.15 NM either. BlueBird's own table is the natural source if the app ever needs the localizer list its receiver uses.
- **No airport link.** In the fs2024 database every KATL `ils` row is an orphan (`loc_airport_ident` NULL), so a
  candidate list must come from a bounding-box query on `ils.laty` / `lonx` (as `OrphanIlsLookup.ReadOrphanCandidates`
  does), not from `loc_airport_ident`. fs2020 has the airport columns filled.
- **Latching.** The value stays after tuning away, so a reading is evidence of the CURRENT tuning only if it tracks the
  current position. At the gate (not moving) a stale value matches the previously received localizer just as well.
  Validity must come from a second source: `B757_SADI_LOC` with the SADI ILS selector on (D1), or a second reading
  after the aircraft has moved.
- **Supplement (geometry only, not measured).** With the aircraft lined up at a runway threshold, a reciprocal pair
  (08L and 26R, 08R and 26L, 10 and 28) both read about 0 degrees (gap 0.00-0.15; 09L / 27R and 09R / 27L differ by
  only 0.6), the other localizer of the same pair reads 4.5-6 degrees off (09L / 09R, 08L / 08R), and the other pair
  reads 17-28 degrees off. So the departure runway's own ILS is a good benchmark for "which parallel", but cannot by
  itself separate it from its reciprocal on the centerline (the reciprocal's antenna is only 250-330 m behind the
  threshold). Given the 27L / 27R case, treat this as geometry only.

## The ILS frequency: findings and ideas

What is known about tuning the 757's ILS frequency (plan 04, Task 6), which is not built yet. On 2026-10-26 the owner
decided to keep investigating it and to try to implement it if possible: the controller is measuring and building the
localizer-benchmark approach (idea 6 below). This section is meant to be read alone. Everything below was measured on
the ground at KATL gate B1 (rounds 1-3 above) unless it says "unmeasured".

**What the pilot needs.** The 757's ILS is tuned by hand on the ILS control panel on the aft pedestal and parked when
it is not needed (the Complete Flight Guide, pp. 43, 61, 168, 192, 201). The FMC does not tune it, the CDU has no line
for it, and no control-map panel holds it, so today a pilot using the app cannot tune the ILS at all. Plan 04's Ctrl+N
window carries the ILS course (`BBS_ILS_COURSE`, a plain writable variable) and the FMC's runway ILS from APPROACH REF
for reference, but not the frequency.

### Why it is hard

- **No variable holds the ILS frequency.** One `-1 (>L:B757_ILS_FREQ_WHOLE_INC)`: full snapshots before and after
  differ in nothing outside noise; the stock `NAV ACTIVE FREQUENCY:1..4` stayed 110.50 and `NAV HAS LOCALIZER:1..3` 0
  with 108.70 tuned. BlueBird's receivers are its own; the stock NAV is unused.
- **The knobs are step requests on rings with no end stop.** `L:B757_ILS_FREQ_WHOLE_INC` and `L:B757_ILS_FREQ_FRAC_INC`
  take +1 or -1 and BlueBird consumes them (the variable reads 0 again within 0.6 s). The whole knob wraps through
  PARK, so there is nothing to home against and no way to tell the state from the request.
- **The display is a WASM gauge picture.** The ILS panel's window is `WasmGaugeLiveView_12` in `VCockpit15` (668 x 93 at
  400,852): a frequency window and a course window. The frequency is nowhere in a variable.
- **MobiFlight drops an identical repeated calculator string** (measured in round 1 with a repeated
  `(>K:COM_STBY_RADIO_SWAP)`; not tried on these L:vars). Step requests sent by the app must go through
  `ExecuteCalculatorCodeUnique`, which makes each write unique (`"{seq} 0 * {rpn}"`).

### What is known

- **The knob model.** Whole knob: a ring with PARK in it, 108 to 109 to 110 to 111 to PARK to 108 for +1, the reverse for
  -1 (leaving PARK, +1 gives 108.xx and -1 gives 111.xx); it never stops at an end. Fraction knob: the ILS channels
  only, `.10 .15 .30 .35 .50 .55 .70 .75 .90 .95` in that order for +1, wrapping `.95` to `.10` inside the same MHz with
  no carry; range 108.10-111.95, 40 channels plus PARK. The whole knob keeps the fraction (108.75 to 109.75). PARK
  remembers the fraction (111.75, PARK, 108.75). The fraction knob does nothing while parked. From a known state any
  frequency is at most 2 whole and 5 fraction steps away. The step-by-step table is in "C. ILS panel on the ground".
- **The load state.** The owner stated that the flight was in its starting panel state when round 1 began (only the
  batteries on and the APU started), so the panel loads at PARK with fraction .10. That is proven by stepping: PARK -1
  gives 111.10, and 111.10 +1 gives PARK. Unproven: other start types (on the runway, in the air, a saved flight), and
  an MSFSBA restart in mid-flight (an app-side state is lost).
- **The ILS course window shows `BBS_ILS_COURSE`** (round 3: `000` to `095` after a direct write of 95).
- **The EHSI shows no frequency on the ground.** Captain's EHSI in APP mode (`B757_ADI_1_CTR_KNOB` 1) with 108.75 and
  with 108.70 tuned: bottom-left `ILS` (green), bottom-right green dashes `------`, top-left `DME ---`, top-right
  `------Z`; no frequency, no ident, no deviation pointer. With a localizer received: unmeasured.
- **No FMC ILS error on the ground** (E3): wrong frequency 110.55, wrong course 105, and PARK, each held 10-20 s, with
  the route, runway and approach programmed, on both CDUs. The manual ties RW/ILS FREQ ERROR and RW/ILS CRS ERROR to
  approach guidance (p. 192). APPROACH REF's ILS lines are the database's and never change with the tuning; on the
  ground they show the departure runway's ILS (E2).
- **No EFB debug or logs** (D2): the Systems app lists only the four diagrams 24, 28, 29 and 36 in this build.
- **Pictures.** Coherent has no `Page.captureScreenshot`, and `canvas.toDataURL` of a live view returns an empty
  string, so a picture of the gauge needs the overlay plus a window capture: an `<img src="WasmGaugeLiveView_12">`
  placed in `VCockpit14` over a visible display (cross-view `src` works; round 1 used the upper EICAS rect 747,8
  859 x 704, a scratch variant of `nd-mirror-on.js` in `docs/b757/tools/live/`), then a window capture
  (`docs/b757/tools/live/capture.ps1`), readable after a 2-3x crop. The camera must be on a view that shows that
  display (instrument view 1, the main panel, does).
- **A received localizer** (D1 and "Localizer geometry"): `BBS_LOC_DEV_DEG` takes a station-specific value that matches
  the geometry, but latches; `B757_SADI_LOC` is live both ways with the SADI ILS selector on.

### Ideas, with their status

1. **Count clicks from the load state.** Keep an app-side model of the panel (the whole ring position and the fraction),
   start it at PARK / .10, and apply each request the app sends. Viable on the owner's statement of the load state;
   unproven for the other start types and an MSFSBA restart, and blind to knob turns the app did not make.
2. **Read the gauge's picture.** Overlay `WasmGaugeLiveView_12` (from `VCockpit15`) on a visible display in another view,
   capture the window, and read the digits, locally or through the app's AI display reads. Viable: the digits were
   read by hand from such captures throughout rounds 1-3 (the camera must be on a view that shows the overlaid
   display). Not built; the cost is a view and a capture per read.
3. **The aircraft as a confirmation.** `B757_SADI_LOC` is live both ways with the standby attitude indicator's ILS
   selector on (`B757_MAIN_M_Gau_STBY_GYRO_SELECTOR` 1; start 0; positions 0-2, the knob face OFF, F/CRS, B/CRS): non-zero
   within 4 s of tuning a received localizer, back to about 0 on anything else (D1). It says a localizer is received
   now, not which. Cost: the pilot sets a cockpit switch.
4. **BlueBird's EFB debug pages and logs.** Absent in this version (D2): the module's strings name an ILS debug page and
   LOGS / SAVE LOGS, but the Systems app does not offer them, and no setting was found.
5. **Ask BlueBird to publish the ILS frequency as a variable.** Open; not asked.
6. **The owner's benchmark (being investigated for Task 6, owner, 2026-10-26).** Tune the departure runway's (or the
   arrival's) ILS and identify the received localizer from `BBS_LOC_DEV_DEG` against the predicted geometry (the table
   in "Localizer geometry"). That anchors the count even when the app started mid-flight. Proven on the ground at
   KATL: the four received localizers matched within 0.15-0.26 degrees and are at least 2.8 degrees apart. The value
   latches, so validity comes from the SADI signal (idea 3) or a second reading after moving; the candidate
   localizers come from the navdata by position (they have no airport link in fs2024); and the departure runway's ILS
   cannot separate itself from its reciprocal on the centerline.

**The design the controller proposed** (not built yet; the benchmark is being investigated for Task 6): count from the
load (idea 1), sync the count by the localizer benchmark (idea 6), say "ILS tuning unknown" when neither holds, and add
the picture read (idea 2) later.

### Open in-flight questions (unmeasured)

- Does the FMC post RW/ILS FREQ ERROR or RW/ILS CRS ERROR in descent or on approach, on which CDU, and how long after a
  mismatch?
- Does the localizer capture (LOC / APP) with the ILS parked, or with a wrong frequency?
- Does APPROACH REF switch to the destination runway in flight (after takeoff, or past some distance), and does `INIT`
  alone open APPROACH REF in descent (Boeing's phase-dependent INIT REF)?
- Does the EHSI's bottom-right field show the ILS frequency or ident once a localizer is received?
- Why were 27L and 27R not received at 0.8 NM (a minimum distance, or a test to repeat)?
- Do `BBS_LOC_DEV_DEG` and `B757_SADI_LOC` follow a received localizer in flight, and clear when the ILS is parked?

**Warning.** Writing the EFIS mode knob variable (`B757_ADI_1_CTR_KNOB`) directly was followed once by VOR L switching
from AUTO to MAN (`B757_MCP_VOR1_MODE_AUTO` 1 to 0; the VOR/DME panel read `117.40 MAN`): measured in round 1 between
11:47:48 and 11:48:32 after putting the knob back from APP to MAP with a direct write. Not reproduced; plausibly
BlueBird's auto-tuning logic around EFIS modes (manual p. 94: AUTO tuning only in MAP or PLAN). It was put back with the
VOR/DME 1 button. Check VOR AUTO afterwards in any experiment that writes the knob.

## Round 4: ILS step timing and localizer detection

Measured 13:09-13:34. Same aircraft and gate, MSFSBA closed, the owner's KATL-KMIA route untouched (no CDU key
pressed). Start (13:09:19): ILS PARK with fraction .10, proved on the gauge (MCP `-1` to 111.10 at 13:14:39, `+1` to
PARK at 13:14:49); `BBS_ILS_COURSE` 0, `BBS_LOC_DEV_DEG` 0, SADI selector 0, `B757_SADI_LOC` 0, ND MAP
(`B757_ADI_1_CTR_KNOB` 2), VOR L and R AUTO. The camera was still round 3's instrument view 1 (type 2 index 1,
substate 5) and was not touched: that view shows the upper EICAS and the SADI, so the ILS gauge overlay (round 1's:
`WasmGaugeLiveView_12` over the upper EICAS in VCockpit14) was read from a window capture at (840,545) 340 x 65.

Method:
- Writes: the MCP calculator (`msfs_execute_calculator_code`, every string unique `n 0 * ...`) where timing did not
  matter; a scratch SimConnect harness where it did. The harness sends what the app sends (default channel
  `MobiFlight.Command`, `MF.SimVars.Set.<seq> 0 * <rpn>`, a 1024-byte string, through the app's own `SimConnect.dll`)
  with a wall-clock timestamp, and reads the same L:vars through a SimConnect data definition every sim frame
  (`SIM_FRAME`, unit `number`: the app's read path). MCP calls take seconds and cannot be spaced 50-150 ms.
- Reads: an in-page poll in the BBSTablet view (a `setTimeout` loop recording `Date.now()` and `E:SIMULATION TIME`,
  started and read back by separate `coherent-eval.ps1` calls). A 20 ms request runs once per sim frame (21-45 ms
  apart, median 34, once 192 ms in the hitch below); a 100 ms request runs every 100-138 ms. Sim time ran with wall
  time (87.546 s in 87.546 s). About 30-31 frames a second (SimConnect frame median 33.4 ms); one 192 ms frame in a
  110 s window (13:15:21.8).
- The Coherent inspector does not await promises (an `awaitPromise` evaluate returned `{}` at once): a timed poll has
  to be started, left running in the page and read back later.

### F1. ILS knob steps through the calculator

F1.1 Consume time (13 MCP writes, 13:14:35-13:15:58): 7 WHOLE +1/-1 (PARK to 111.10 and back x3, then PARK to
108.10) and 6 FRAC +1/-1 at 108.10 (.15 and back to .10 x3). Each request read non-zero in exactly one in-page sample
and 0 in the next: first non-zero to first 0 = **29 / 33 / 35 ms** (min / median / max), i.e. one sample, one frame. A
request is consumed in the frame it lands in or the next. **Through SimConnect it is never seen**: the per-frame
data-definition read showed the request at 0 in all 8,485 frames of every harness run (77 writes in their windows).
Every step landed on the gauge.

F1.2 Bursts of 6 FRAC +1 (harness; every result read on the gauge):

| Run (sends) | Spacing | Gauge, start to end | Steps applied | In-page view |
|---|---|---|---|---|
| handshake, 13:19:28.96-29.46 | each sent once the page saw the previous request non-zero and then 0 (through the Coherent inspector): 99-102 ms | 108.10 to **108.70** | **6/6** | arm 23-38 ms, send to non-zero 28-29 ms, non-zero to 0 32-38 ms |
| 13:19:48.58-49.33 | 150 ms | 108.70 to **108.30** | **6/6** | six separate one-frame pulses |
| 13:20:10.31-10.56 | 50 ms | 108.30 to **108.90** | **6/6** | pulses run together (non-zero for 2 samples at a time) |
| extra, 13:20:47.71-47.81 | 20 ms | 108.90 to 108.30 (6 steps: 108.50) | **4/6** | non-zero from +26 to +158 ms |
| extra, 13:21:09.164-.169 | 1 ms | 108.30 to 108.35 (6 steps: 108.95) | **1/6** | one pulse |

F1.3 One request of 2 (`4301 0 * 2 (>L:B757_ILS_FREQ_FRAC_INC)`, 13:21:49): it read 2 for one sample, then 0, and the
gauge moved **one** channel, 108.35 to 108.50. Put back with FRAC -1 to 108.35 (13:22:17).

F1.4 FRAC -1 x3 and WHOLE -1 (harness, 400 ms apart) to PARK (13:22:34); proof -1 to 111.10, +1 to PARK (13:22:41,
13:22:47). Side effect, expected: the 50 ms burst ended on 108.90 (KATL 09R), so `BBS_LOC_DEV_DEG` latched -18.498 as
in round 2; left through F2 and written 0 at the end.

Verdict F1:
- A request is consumed within one frame. **One write is one step whatever its size** (+2 moved one channel), so
  steps cannot be batched into one write, and **two writes landing in the same frame count once** (1 ms apart: 1 of
  6; 20 ms: 4 of 6). 50 ms (1.5 frames here) and 150 ms lost nothing; nor did the handshake.
- The safe gap is "more than one frame". 50 ms leaves no margin below 20 fps, and one long frame (192 ms was seen)
  would bring two queued writes into the same frame. Proposal: 150 ms or more between steps (2 frames down to
  ~13 fps). The handshake through the BBSTablet view (about 100 ms a step) does not depend on the frame time, but needs
  the Coherent link the app already has for the CDU tablet agent.
- The app cannot confirm a step, or handshake, through SimConnect: the request never reads non-zero there.

### F2. Localizer detection after tuning, SADI selector on

The SADI ILS selector was set with MCP `4501 0 * 1 (>L:B757_MAIN_M_Gau_STBY_GYRO_SELECTOR)` (13:23:31; read back 1;
the knob is drawn turned from OFF to ILS, the panel labels being OFF / ILS / B/CRS). PARK to 110.35 by harness
(13:23:48-49, confirmed on the gauge). Each measured step was a harness write, with the page poll (100 ms) and the
harness's per-frame SimConnect read running. Times are from the step's send.

| Step (send) | `BBS_LOC_DEV_DEG` | `B757_SADI_LOC` | `...STBY_GYRO_knob` | `B757_SADI_LOC_FLAG` |
|---|---|---|---|---|
| F2.1 110.35 to 110.50 (13:24:06.99) | -18.498 to -6.643 at **+2577 ms** | 0 to 3.5 at +2611 (one frame later), 25 at +2.91 s, 47.5 at +4.0 s, 50.0 by ~+7 s | 0 to 0.38 at +2641, then 4.75 | 0 throughout |
| F2.2 110.50 to 110.55 (13:25:02.11) | last change at +17 ms, stays -6.6425 | falls from **+86 ms**: < 25 at 0.39 s, < 10 at 0.86, < 5 at 1.19, < 2 at 1.63, **< 1 at 1.96**, < 0.5 at 2.30, **< 0.1 at 3.07**, < 0.01 at 4.17, < 0.001 at 5.28 | the same curve (= SADI x 0.095) | 0 |
| F2.3 110.55 to 110.50 (13:25:55.01) | live again at **+3088 ms** (-6.642504277 to -6.642504350) | 7.2 at +3148 (page), 32 at +3.6 s, 49.9 at +6.0 s | follows | 0 |
| F2.4 108.75 to 108.90 (13:26:52.74) | -6.643 to -18.498 at **+2662 ms** | 3.2 at +2700, 25 at ~+3.0 s, 50 at ~+8.4 s | follows | 0 |

The page poll agreed within one sample (DEV +2571 / +3148 / +2653, SADI +2702 / +3148 / +2988). F2.4 was reached by
WHOLE -1 x2, FRAC +1 x3 (110.50 to 108.75, 13:26:27-29, nothing received on the way) and 8 s of decay. Every tuned
state was confirmed on the gauge.

- **`B757_SADI_LOC` is a first-order lag toward a target** (time constant 483 ms, half-life 335 ms, fitted over 260
  frames of F2.2; the rise has the same time constant): 50 on 09L and 09R, about 19 on 08R. After a loss it heads for
  0 within 2-3 frames (76-105 ms in 6 switches); after a tuning it moves up only when a station is acquired.
- **`BBS_LOC_DEV_DEG` is live while a localizer is received and freezes on loss**: while received it changes every
  frame in the 8th-10th decimal (167, 157 and 165 distinct values in F2.1, F2.3, F2.4) and it stops within a frame of
  the loss. On re-reception of the same station it moves only in the 8th decimal, so to any tolerance of 1e-6 or more
  (the app's is 0.001, [SIM-7]) it does not change. At rest on the ground the live changes are about 1e-9 degrees a
  second; in flight they should move visibly (unmeasured).

Extra, the acquisition delay (same knob writes): 108.90 (09R) to 109.90 (08R) and back by WHOLE +1/-1, the delay taken
to the `BBS_LOC_DEV_DEG` jump.
- 6 switches 6.0 s apart (13:28:44-13:29:14): 2790, 2816, 2844, 2872, 2900, 2928 ms (+28 ms each).
- 8 switches 6.4 s apart (13:30:49-13:31:34): 830, 462, 91, 2745, 2380, 2005, 1633, 1264 ms (-372 ms each, wrapping).
- Acquisitions were always exactly 186 or 279 frames apart (multiples of 93 frames, 3013-3018 ms here). **A new
  localizer is picked up only at a periodic scan, every ~3.0 s**: the delay after the last step is anywhere from about
  0.1 to 3.1 s (91-3088 ms over 17 tunings). Whether the scan counts frames (93) or seconds cannot be told at a steady
  frame rate; if frames, it stretches at a lower frame rate (about 4.7 s at 20 fps).
- So F2.3's slower return (3.09 s against F2.1's 2.58 s) is the scan's phase, not something about re-reception.

F2.5 MCP calculator read while 108.90 was received (13:27:19; live 50.0 in the page and through SimConnect):
`(L:B757_SADI_LOC)` gave **100** (twice; `msfs_get_lvar` 100; `(L:B757_SADI_LOC, number)` 1000). The same MCP
session read `BBS_LOC_DEV_DEG` 0 (live -18.498), the gyro knob 0 (live 4.75) and `B757_ADI_1_CTR_KNOB` 0 (live 2).
After `msfs_disconnect` and `msfs_connect` (13:28): `(L:B757_SADI_LOC)` **50**, `(L:BBS_LOC_DEV_DEG)` -18.49825,
`(L:B757_ADI_1_CTR_KNOB)` 2. The calculator read returns the live value; the MCP's earlier session had gone stale (the
known MCP problem). **The app's own path, a SimConnect data definition on `L:B757_SADI_LOC`, returned the live value
in every frame** (49.999999999999964, identical to the page; the same for the gyro knob and `BBS_LOC_DEV_DEG`).

Verdict F2:
- Detection: a received localizer appears 0.1-3.1 s after the step that tunes it (the ~3 s scan), `BBS_LOC_DEV_DEG`
  first and `B757_SADI_LOC` one frame later (3.2-3.5 in its first frame, 25 or more after 0.33 s on 09L/09R).
  `B757_SADI_LOC_FLAG` never moved: no use.
- "Gone": from 50 the SADI value is below 1.0 1.96 s after the step and below 0.1 at 3.07 s (from 19, below 1.0 after
  about 1.5 s).
- Proposal: read `B757_SADI_LOC` **3.5 s after the last tuning step** (the 3.0 s scan plus margin; longer at a low
  frame rate if the scan counts frames): **1.0 or more = received**, less = not received. At 3.5 s an old station's
  value has fallen from 50 to about 0.04, and a station acquired at the latest (3.1 s) is already above 10 (08L/08R
  targets ~18-19, 09L/09R 50).
- `BBS_LOC_DEV_DEG` says which station: it takes the new station's value at acquisition (09L -6.643, 09R -18.498,
  08R 18.227) and keeps it after a loss.
- The SADI value needs the SADI's ILS selector on (ILS or B/CRS), a cockpit switch.

### Round 4 state at the end (13:34)

| Value | Start | Changed to | Restored | Confirmed |
|---|---|---|---|---|
| ILS panel | PARK, fraction .10 | 111.10 ... 108.90, 110.50, 109.90 (F1, F2) | PARK, fraction .10 (FRAC +1 x2, WHOLE -1; proof -1 to 111.10, +1 to PARK) | gauge 13:32:52, 13:32:58, 13:33:05 |
| `B757_MAIN_M_Gau_STBY_GYRO_SELECTOR` | 0 | 1 | 0 (MCP `4701 0 * 0 (>L:...)`) | page 13:33:21, snapshot 13:34:04 |
| `BBS_LOC_DEV_DEG` | 0 | -18.498, -6.643, 18.227 | 0 (MCP `4702 0 * 0 (>L:BBS_LOC_DEV_DEG)`) | page 13:33:21, snapshot 13:34:04 |
| `B757_SADI_LOC`, gyro knob | 0 | up to 50 / 4.75 | 4.5e-66 / 4.3e-67 (decaying on their own) | snapshot 13:34:04 |
| `BBS_ILS_COURSE` | 0 | not written | 0 | snapshot 13:34:04 |
| VCockpit14 overlay `msfsba_mirror2` | none | ILS gauge mirror | removed (`removed`; `nd-mirror-off.js` answered `none`) | 13:33:30 |
| BBSTablet globals `__msfsba_r4p`, `__msfsba_r4hs` | none | poll and handshake state | deleted (`undefined`) | 13:33:30 |
| MCP SimConnect session | connected, reads stale | disconnected and reconnected (F2.5) | connected | 13:28 |

Full snapshot 13:09:19 to 13:34:04: only `B757_SADI_LOC` and the gyro knob differ (decaying toward 0, as in round 2).
The camera (instrument view 1), CDUs, EFB, COM and VOR radios were not touched (`B757_ADI_1_CTR_KNOB` 2, VOR L and R
AUTO at the end). No Coherent socket open; the harness closed its SimConnect connection after every run. The CDU
tablet agent is still installed, as before.

## Round 5: reading the ILS display; the localizer on 09L

Measured 13:51-14:08. KATL runway 09L, lined up by the owner with MSFSBA's runway teleport: 33.63473137 /
-84.44822173, true heading 090.0025°, on the ground, ground speed 0, parking brake set (read 13:51:56, 14:06:15 and
14:08:28, the same each time). MSFSBA closed. Start (snapshot 13:52:40): ILS PARK with fraction .10 (proved by a display
read, see G1: -1 to `111.10`, +1 to `-----`, 14:00:26-14:00:29), `BBS_ILS_COURSE` 0, `BBS_LOC_DEV_DEG` 0, SADI
selector 0, `B757_SADI_LOC` 0, ND MAP, VOR L and R AUTO.

**The camera was NOT on the normal cockpit view**: the first capture and `CAMERA VIEW TYPE AND INDEX:0/:1` = 2/1
(state 2, substate 5) show round 3's instrument view 1, the main panel (MCP, EICAS, SADI). It was left there.

Method: writes (ILS knob requests, `BBS_ILS_COURSE`, the SADI selector, `BBS_LOC_DEV_DEG`, the camera index) are
`SimVar.SetSimVarValue` in the BBSTablet view, with knob steps 300 ms apart. Reads are in the same view. The overlay
goes in the view named below. No MCP call was made.

### G1. Reading the ILS display from the 2D UI layer

G1.1 The view. These 2D views in `pagelist.json` cover the whole window (1920 x 1080): `mainUi`
(`coui://html_UI/Global/mainUI.html`), two `notifications` pages and `transition`. These are smaller than the window:
`Toolbar` (918 x 162), `Mission Steps` (389 x 240) and three untitled `InGameHud` pages (324 x 324, 1296 x 540,
1080 x 540).

**The first candidate tried, `mainUi`, works.** The overlay is a `position:fixed` div at left/top 16 px, z-index
2147483647, `pointer-events:none`, black, holding `<img src="WasmGaugeLiveView_12">` at 668 x 93. A window capture
shows the live ILS gauge in it: the live view resolves in a UI view, as it did cross-view in round 1.

It lands at client pixels (16,16)-(683,108), exactly 668 x 93. The page reports `--uiScale` "1", `devicePixelRatio` 1
and innerWidth x innerHeight 1920 x 1080. The window is borderless, 1920 x 1080 at (0,0), client origin (0,0), 96 DPI.
So 1 CSS px = 1 client pixel here. Other UI scales, resolutions and DPI were not measured; an app could map with client
width / innerWidth, which the inject answer returns.

The other candidates were not needed and not tried. No app code uses `mainUi` today.

G1.2 Contrast: a CSS filter on the img only. Coherent applies the filters live.

| Filter | Result |
|---|---|
| none | grey lit segments (channel mean ~64-95) and dim unlit "ghost" segments (~20-35) on navy (13,13,26); 98% of pixels in between |
| `grayscale(1) contrast(4)` | all black, no digits |
| `invert(1)` | ghosts kept, light ground |
| `grayscale(1) brightness(2) contrast(10)` | clean but thinner segments; 2.75% grey |
| `grayscale(1) brightness(2.8) contrast(10)`, with or without `invert(1)` | white on black (or black on white); 0.98% grey |
| **`grayscale(1) brightness(2.6) contrast(40)`** (chosen) | white digits on black, ghosts gone; **99.44% of pixels pure black or white**, 348 edge pixels in between |

The inverted version is as clean, but it flashes a white box on screen, so the black one was chosen.

G1.3 Camera independence:
1. Read `109.35 095` in the start view (2/1).
2. Wrote index 0 (14:03:13); it read back 2/0 at +1.7 s and +3.5 s: the captain's ADI/EHSI view.
3. Read again. **The crop at (16,16), 668 x 93, is byte-identical to the 2/1 one** (the same PNG file) and decodes
   the same. A full capture shows the box at the top left over that view.
4. Wrote index 1 back at 14:03:34; it read back 2/1 at +1.7 s and +3.5 s, and the capture is framed as at the start.

The normal cockpit view (type 1) was not tried: the camera was not on it, and round 3 found that this aircraft refuses
type writes out of the instrument type.

G1.4 The decoder: a scratch Python script, standard library only.

The cells were measured from a capture with every segment lit (the filter `grayscale(1) brightness(7) contrast(10)`
turns the ghosts white):
- Frequency cells at x 11, 73, 134, 195 and 256; course cells at x 420, 482 and 543. Each is 48 px wide, pitch ~61.
- Segments: a y 12-23, f/b y 15-49, g y 45-56, e/c y 51-86, d y 78-89.
- The decimal point (x 184-192, y 79-88) is drawn only when a frequency is tuned; it has no ghost.

Method:
1. Resample to 668 x 93.
2. Otsu threshold.
3. Try offsets of +-4 px and keep the one where every segment is most clearly on or off.
4. Sample each segment's core, relative to the cell's left edge x0; a segment is on at 50% or more.
   - a: x0+14..33, y 15..20
   - b: x0+38..44, y 22..41
   - c: x0+38..44, y 58..78
   - d: x0+14..33, y 81..86
   - e: x0+3..9, y 58..78
   - f: x0+3..9, y 22..41
   - g: x0+14..33, y 48..53
5. Map the pattern to a character. In this font 6 = acdefg, 7 = abc, 9 = abcfg (no bottom) and a dash = g.

| State (knob model or course write) | Decoded |
|---|---|
| PARK | `----- 000` |
| W +1 | `108.10 000` |
| W +1 x2, F +1 x4 | `110.50 000` |
| W +1, F +1 x5 | `111.95 000` |
| W -1 x2, F +1 x4 | `109.35 000` |
| `BBS_ILS_COURSE` 95 | `109.35 095` |
| F +1 x4, course 246 (extra, for digits 2, 4, 6 and 7) | `109.75 246` |
| course 0 (its start value) | `109.75 000` |
| from G2 and the restore | `111.30 000`, `111.10 000` |

- **All 19 reads with the chosen filter decoded right, with clarity 1.00** (every segment fully on or off).
- So did these:
  - the unfiltered crop (Otsu threshold 45)
  - inverted crops
  - crops cut from the two full captures
  - a crop cut 3 px left and 2 px low (offset found: (3,-2))
- A crop with an 8 px margin fails: the decoder expects the overlay's rectangle.
- The bare scene under that corner decodes as invalid, with clarity 0.21-0.33. So "the overlay is drawn" can be told
  from the decode itself.

G1.5 Timing. The figures come from 26 timed reads, timed from the inject call. Each read is one inject, then
PrintWindow captures of the 1920 x 1080 client area back to back until the box is drawn, a removal at once, then
captures until the box is gone.
- The inject call was answered in 28-38 ms (median 35). Every Coherent call costs about one UI frame: pagelist
  31-35 ms, socket connect 31-36 ms, an evaluate 33-35 ms.
- **The box was in the third capture every time.** It was not in a capture that started 74-92 ms after the inject,
  and it was in one that ended 134-153 ms (median 145) after it.
- The capture itself: 35-51 ms (median 44) for the first after an inject, 21-43 ms (median 27) for the rest. That is a
  32-bit DIB with no PNG encoding; the app's PNG encoding of a full frame would add to it.
- The removal was sent at 141-159 ms and answered at 163-174 ms (median 167).
- **One read takes about 0.17 s** with the socket open and the page id known, or about 0.24 s with the pagelist and the
  connect. The decode adds tens of milliseconds in Python.
- The box stayed in two more captures (the last starting at 208-225 ms). It was gone from every capture ending
  268-286 ms after the inject.
- **On screen for at least 71-75 ms and at most 191-210 ms per read: about 130 ms, some 4 frames at ~30 fps.**
- Exceptions: the first exploratory inject, done as separate processes, stayed up ~0.6 s. The `contrast(4)` test (all
  black, never detected) stayed up 3.1 s, until its timeout.
- What a pilot would see: a black 668 x 93 box with white digits in the top-left corner of the sim window, for about
  0.13 s.
- It never shows on a cockpit display. Only `mainUi` holds it, no VCockpit view got an element, and the cockpit
  displays in the full captures look as they do without it.
- Not measured: VR, the sim's hide-UI toggle, a menu or window over that corner, other UI scales and resolutions.

G1.6 Each read removed its element (answer `removed:1, left:0`). At 14:08 a query of all 16 views found:
- no `msfsba*` element;
- no `WasmGaugeLiveView` img outside the VCockpit pages' own (18, 10 and 2);
- no new global. The round-1 CDU agent's and round-3 EFB agent's globals remain, as before.

Verdict G1:
- **Yes: the ILS display can be read without depending on the camera.** The ILS gauge's live view, laid on `mainUi` at
  (16,16), filtered with `grayscale(1) brightness(2.6) contrast(40)`, captured and decoded, gave the right frequency
  and course in every state.
- It sat at the same client pixels in two different camera views, and the two crops were byte-identical.
- One read costs about 0.17-0.24 s, and the box is on screen for about 0.13 s.
- A valid frequency and course with clarity 1.0 tells "drawn" from the bare scene.

### G2. The localizer lined up on 09L

G2.1 The position is as above (true heading 090.0025°). The SADI selector went 0 to 1 at 14:06:28 (read back 1). With
109.75 tuned, `B757_SADI_LOC` stayed 0 for 4.5 s.

G2.2 110.50: W +1, F -1 x3 from 109.75, last click at 14:06:42.42; the gauge read `110.50 000`. The page was polled
every ~0.1 s for 7.9 s.
- **Received.** `BBS_LOC_DEV_DEG` went 0 to -0.03975° between +2.02 and +2.16 s after the last click (the ~3 s scan).
  `B757_SADI_LOC` read 6.2 at +2.16 s and **28.9 at +3.5 s**, settling at 30.45. The gyro knob went to 4.75; the LOC
  flag stayed 0.
- **Deviation -0.040°.** Predicted **-0.053°** from the brief's antenna (33.63470556 / -84.4053, course 090°, 3,974 m
  = 2.15 NM away), or -0.071° from the app's fs2024 navdata (33.634701 / -84.405182, course 089.99°). That is within
  0.01-0.03° of the prediction, ~0.04° left of centre. The value is live: 48 different values in 66 samples, moving
  in the 9th decimal.
- `B757_SADI_LOC` behaves like a needle position around ~30, not a signal strength. On the centreline it settles at
  30.45; at the gate it was 50 for 09L (-6.6°) and 18-19 for 08L and 08R (+18° and +26°). It is 0 when nothing is
  received, so round 4's "1.0 or more = received" still holds.

G2.3 111.30: W +1, F -1 x2, last click at 14:07:15.82; the gauge read `111.30 000`.
- **Not received.** `B757_SADI_LOC` fell from 30.45 once 110.50 was left, 0.64 s before the last click. It read 8.7 at
  the last click, 0.13 at +2.06 s, 0.006 at +3.5 s and 1e-7 at +9 s.
- `BBS_LOC_DEV_DEG` stayed at -0.0397542 (09L's value, frozen) through 9 s, which is three scans.
- **The 27R antenna is 283.5 m (0.153 NM) from the aircraft** by the brief's coordinates (294 m, 0.159 NM, by the
  navdata). The aircraft is on its front-course side; predicted deviation +0.52° (+0.58° by the navdata).
- With round 2 (27L and 27R not received at 0.8 NM; every received localizer at least 1.09 NM away), 27R has now not
  been received at either 0.15 or 0.82 NM.

During G1, 110.50 was tuned for 2.07 s (14:02:10.6-14:02:12.7) and `BBS_LOC_DEV_DEG` stayed 0: no scan fell in that
window. This fits round 4's ~3 s scan.

### Round 5 end state (14:08)

| Value | Start | Changed to | Restored | Confirmed |
|---|---|---|---|---|
| ILS panel | PARK, fraction .10 | 111.10 ... 111.30 (G1.4, G2) | PARK, fraction .10 (111.30, F -1 x2 to 111.10, W +1 to PARK; proof -1 to `111.10`, +1 to `-----`) | display read 14:07:42-14:07:48 |
| `BBS_ILS_COURSE` | 0 | 95, 246 | 0 | display read `000` 14:04:41; snapshot 14:08:28 |
| `B757_MAIN_M_Gau_STBY_GYRO_SELECTOR` | 0 | 1 | 0 | page 14:07:57 (held 3.8 s); snapshot 14:08:28 |
| `BBS_LOC_DEV_DEG` | 0 | -0.03975 | 0 (direct write, 14:07:57.7) | held 3.8 s; snapshot 14:08:28 |
| `B757_SADI_LOC`, gyro knob | 0 | up to 30.45 / 4.75 | ~1e-65 (decaying on their own) | snapshot 14:08:28 |
| Camera | 2/1 (instrument view, main panel) | 2/0 | 2/1 | read back 14:03:36, 14:03:38 and 14:08:28; end capture |
| `mainUi` overlay `msfsba_ocr5` | none | 27 timed reads plus one exploratory inject | removed after each | query of all views, 14:08 |

Full snapshot 13:52:40 to 14:08:28:
- Besides the decaying SADI values, only **`B757_FC_SPOILER_LEVER` 0.8149 to 0.7325** differs.
- It moved between 13:52:40 and 14:05:23 with no write by the agent. No write in this round touches the spoilers, and
  round 2 saw this variable move on its own too. It was stable at 14:06 and was not touched.
- Position and heading are unchanged; ND MAP, VOR L and R AUTO.
- No Coherent socket is left open (0 ESTABLISHED on :19999).

## Labelled gauges

A read of the DOM of the two VCockpit pages (read only; the controller, 2026-10-26, after round 5). Every BlueBird gauge
is a `WASM-INSTRUMENT` element whose `url` carries `wasm_gauge=<name>` and whose picture is an
`<img src="WasmGaugeLiveView_N">`, so a gauge can be found by name; the number N can differ per load. The first read
cut each url at 120 characters, so it recorded only the names' first eight characters (`ils_cont` for `ils_control_lcd`),
and the app's gauge search, written from it, looked for `wasm_gauge=ils_cont` exactly and never found the panel (the
owner's first in-flight use, 2026-10-26 evening: "ILS tuning unknown"). A second read that evening gave the full names
below, which match `docs/b757/evidence/package/displays.tsv`; the search now uses `ils_control_lcd`.

| View | Gauges (live view where it was read) |
|---|---|
| `VCockpit15` (18 small panel gauges, `_0` to `_17`) | `ils_control_lcd` (`_12`, 668 x 93), `vor_control_lcd_l`, `vor_control_lcd_r` (`_7`, `_8`), `radio_tuning_panel_lcd_l`, `_r`, `_c` (`_9`, `_10`, `_11`), `adf_control_lcd_l`, `_r`, `xpdr_control_lcd`, `rdmi_lcd_l`, `_r`, `clock_lcd_l`, `_r`, `adi_dh_ref_lcd_l`, `_r`, `fuel_qty_lcd_display`, `aircon_display`, `sei_lcd` |
| `VCockpit14` (the main displays) | `pfd_l`, `pfd_r`, `nd_l`, `nd_r`, `cdu_l`, `cdu_r`, `eicas`, `eicas_lower`, `efb_l`, `irs_display` (full names from `displays.tsv`) |

In flight on 2026-10-26 (FL275, about 100 NM from KMIA), the same read set the owner's ILS by hand through the knob
requests: the panel showed `-----` (parked) with course 090; one MHz click down showed 111.10 (the hidden fraction was
.10); one more MHz click down and two fraction clicks down gave `110.90   090`, read back from the picture.

What it tells later plans: the ILS read (the gauge's picture laid on `mainUi`, filtered, captured and decoded, round 5)
can reach any of these displays by name, not only the ILS panel. The `VCockpit15` panels are mostly LCDs like the ILS
one (the VOR, radio tuning, ADF, transponder and RDMI panels, the clocks and the fuel quantity); only the ILS panel's
seven-segment decoder exists, and the other panels' fonts and layouts are unmeasured. Round 1 had already read `_4` (the
transponder, `ATC L 1200`), `_5` and `_6` (the ADFs, `0190.0`) and `_17` (the fuel quantity panel) by what they showed
(see C).

## Round 6: the display read at night on a 3840 x 2160 sim

Measured 2026-10-27, 18:45-19:15 local, in Robin's flight paused in cruise at dusk (FL270, MCP heading 005). His
build (744b4ccec) said "ILS tuning unknown: the ILS display could not be read." at every set, and debug.log held
"ILS display: timed out (capture/decode)." for every read from 17:29 on. Robin closed MSFSBA for the measurements, so
`mainUi` was free; the overlay was laid there exactly as the app lays it.

Setup (`UserCfg.opt`, MS Store FS2024): FullscreenBorderless 1, Resolution and FullScreenResolution 3840 2160, DLSS
DLAA, DLSS frame generation on, PrimaryScaling 1. The window and its client area are 3840 x 2160 at (0,0); `mainUi`
reported 3840 x 2160 CSS px, devicePixelRatio 1, `--uiScale` 1, so one CSS pixel is one pixel, as in round 5.

H1. Capture cost (PrintWindow full content, as `ScreenshotService` takes it): PrintWindow 80-96 ms, the service's PNG
encode 310-320 ms (8 MB), the reader's PNG decode 41 ms: about 0.48 s a capture. The read's cap (1000 ms, with
150 ms of settle first) had room for one capture: debug.log's timestamps show the first capture starting about 190 ms
into each read and the second about 720 ms in, which the cap ended. Cropped in memory, with no PNG, a capture took
63-85 ms.

H2. The box is drawn in time: in a capture that ran from 99 to 163 ms after the inject (round 5: ending 134-153 ms
after it), and in every capture after that. Paused; in flight unmeasured.

H3. The decode refused it. The panel showed 108.10, course 223 (one megahertz click out of PARK, from the 18:32
set). Through round 5's filter the decoder found every cell right, `10810223` with the point, every segment
0.99-1.00 on or 0.00 off, but the point's core (185-191, 81-87) was 69 % lit: clarity 0.39, under the 0.6 gate, so
null. The filtered point is a round dot of x 185-191, y 80-86 (round 5's daylight: x 184-192, y 79-88).

H4. Why: the panel is dimmer at night. Unfiltered, its lit segments peaked at grey 63 (round 5's daylight: 64-95),
and 60 % of the box was black. The filter `grayscale(1) brightness(2.6) contrast(40)` turns every pixel under about
48 black whatever the panel's brightness, so at night more of each glyph's anti-aliased edge goes and the dot
shrinks inside the core. The decoder's own Otsu threshold follows the picture: 29 that night (45 for round 5's
daylight PARK), and the same display unfiltered read `108.10 223` at clarity 1.00 in every capture.

H5. The fix (plan 04's reader, `CoherentBbs757IlsDisplay`): the overlay is laid unfiltered, and each capture is a
bitmap straight from PrintWindow, cropped in memory, with the screenshot service's own capture (and its screen-copy
fallback) only when PrintWindow fails or comes back blank; the sim window is found once and kept. The fixed reader,
run against the paused flight with MSFSBA closed: three reads, each `108.10 223`, in 525 ms (the first, with the gauge
search and the connect), 289 ms and 255 ms; each capture with its decode 72-99 ms, the first capture deciding every
read. No box was left in `mainUi` and no socket on :19999 afterwards.

Fixtures from this round (668 x 93 crops): `raw_108.10_223_night.png` (unfiltered) and
`oldfilter_108.10_223_night.png` (through round 5's filter), under `tests/MSFSBlindAssist.Tests/Fixtures/bbs757-ils/`.

Not measured: the box's draw time in flight (H2 was paused), daylight unfiltered with a frequency tuned (round 5's
unfiltered fixture is PARK), other resolutions and UI scales, HDR.

## Round 7: the display read in a window that is not 16:9

Measured 2026-10-27, 18:50-19:10 local, on Declan's MSFS 2024 (1.9.21.0) with the BlueBird 757-200 PW N/W parked cold
and dark at night (`E:TIME OF DAY` 3), MSFSBA closed. Plan 04's own reader (`CoherentBbs757IlsDisplay` and
`Bbs757IlsOcr`, `B757` at 924129a8) was run from a scratch xUnit test against the live simulator; writes were
`SimVar.SetSimVarValue` in a VCockpit view.

Setup (`UserCfg.opt`): Windowed 1, Resolution 1278 1360, DLSS ultra performance, no frame generation, VSync on, HDR off,
PrimaryScaling 1. The window was 1294 x 1399 at (1273,0) on a 2560 x 1440 screen (snapped to its right half), its client
1278 x 1360 at (8,31) in the capture.

J1. Every read was refused. `mainUi` answered 1278 x 719 CSS px (`--viewportHeight` 719, devicePixelRatio 1, `--uiScale`
1) in a 1278 x 1360 client, so `OverlayCrop` found no one scale and returned null: debug.log said "the overlay's box
does not fit the capture at the page's scale" at every read, and every set went by the count.

J2. Where the page is. Two 668 x 93 marker boxes were laid in `mainUi`, at CSS (16,16) and at (innerWidth - 684,
innerHeight - 109), and captured by PrintWindow and by a screen copy. The first was at capture x 24-690, y 367-459, the
second at x 602-1268, y 961-1053, the same in both captures. So the page is drawn at one scale (one CSS pixel is one
pixel here) and centred: client y + 320, which is floor((1360 - 719) / 2). In a window taller than 16:9 the simulator
draws its UI page 16:9, letterboxed. Wider than 16:9: not measured.

J3. The fix (round 7's commit). When the page does not fill the client at one scale, `OverlayCrop` fits it in the client
and centres it, but only with the client area known; a page that fills the client takes exactly the old path. Checked
against a copy of the old function on 2,286 sizes the old one accepted (clients 1024-3840 px wide, UI scales 1 to 3,
with and without a window frame, client known and not): every crop identical. With no client area every refused case
is still refused.

J4. Live, with the fix, at night, unfiltered. The battery on (the ILS panel is on standby power), then:

| State (knob or write) | Read |
|---|---|
| PARK (the load state) | `----- 000` |
| WHOLE +1 | `108.10 000` |
| FRAC +1, `BBS_ILS_COURSE` 95 | `108.15 095` |
| course 0, FRAC -1, WHOLE -1 | `----- 000` |

With the panel unpowered every read was "no reading" (the dark display decodes as nothing, never a guess).

J5. Timing on this machine (open, not changed by the fix). The box appeared 0.35-0.5 s after the inject on most reads
(round 5: by 0.15 s), usually in capture 3, about 0.5 s in, close to the read's limit of four captures; back-to-back
reads found it in capture 1. The first read of each test process failed three times out of three: it spends part of
its 1 s cap on the page list, the socket and the gauge search, and ran out after two captures without the box. In the
app the socket stays open, so this costs the first read of a session, which falls to the count. Candidates: a longer
cap for a read that has to connect, or captures kept going to a longer cap instead of stopping at four.

J6. The count's load state is not BlueBird's every preset. The factory panel states (`Data/panel_states/*.snap`, the
EFB's presets) store the ILS as `[whole, fraction]` after the two VOR entries (`[true,117,40]`), whole -1 for PARK:
`approachbaseleg` and `beforelandingflaps30` load 110.30, `quickturn` PARK with .30 hidden, the other six PARK with .10.
The display read recovers from this (a set's second try plans from the read), but `Bbs757IlsDial.LoadState` (PARK, .10)
is wrong after one of those presets, and so is a count used when the display cannot be read.

Put back: the ILS PARK with .10 (proved by the reads), `BBS_ILS_COURSE` 0, the battery off. The `ils-display-fail*.png`
files the test reads left in the logs folder were deleted. No element with an id starting `msfsba` was left in `mainUi`
or the VCockpit pages, and no socket on :19999.

## Round 8: the ILS display read in MSFS 2020 (2026-10-28, Declan's PC, the 757-200 PW and RR)

- **Every frequency set failed in MSFS 2020** with "ILS tuning unknown: the ILS display could not be read". The log
  said "the overlay was not laid (no answer from mainUi)" each time. The course (a direct write) worked.
- **MSFS 2020 has no `mainUi` page.** Its `/pagelist.json` listed NOTIFICATION, TooltipAtlas, Flight Briefing,
  Flight planning, Transition, ActivitiesBackground, the aircraft's VCockpit pages, VLogic, Pause, InGameMarkers (two),
  ToolBar, VFR MAP and the HUD pages, and no `coui://html_UI/Global/mainUI.html`.
- **The toolbar page takes the box instead.** A coloured test box laid at CSS (16,16) on each of InGameMarkers,
  ToolBar and Pause: only the ToolBar one showed in a PrintWindow capture of the 2560 x 1440 window (its page
  answered 2560 x 1440 at devicePixelRatio 1). The ILS gauge's live view laid there showed the panel display
  (course 250).
- **With the reader on the toolbar page (FS2020 only):** "ILS 110.50 set" by the pilot through Input Ctrl+N, every
  capture decoded ("capture 1 at 154 ms took 132 ms with its decode: read"). A 2024 read is unchanged: the toolbar
  page is used only by a reader made for FS2020 and only with no `mainUi` listed (pinned by
  `The_box_goes_on_mainUi_and_on_the_toolbar_page_only_in_FS2020_without_one`).
