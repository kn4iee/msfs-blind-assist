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

## Round 2

(to follow)
