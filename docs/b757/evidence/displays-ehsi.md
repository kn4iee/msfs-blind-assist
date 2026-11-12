# The EHSI's map and plan from memory: what plan 26 reads (plan 26, Task 1)

Measured 2026-11-11, about 15:50 to 17:15 local, MSFS 2024, the BlueBird 757 "BlueBird 757-200 RR N/W 4 Door" parked
at a KSEA gate on external power, BBS Nest running, MSFSBA closed. Memory was read only
(`PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ`, the private b757-memory probe scripts and scratchpad scripts
built on them); no snapshot was kept. Writes to the simulator, at the owner's word ("it's yours to do with what you
like"): the Captain's EFIS mode and range (`L:B757_ADI_1_CTR_KNOB`, `L:BBS_ND1_Range_Index`), CTR and DATA
(`L:B757_ADI_1_CTR_BUTTON`, `L:B757_ADI_1_DATA_BUTTON`), the heading reference (`L:B757_MAIN_HDG_REF_SWITCH`), the MCP
heading (`L:BBS_AP_TARGET_HEADING`), the three IRS selectors to NAV with the tablet's IRS rapid align, and a route on
the Captain's CDU. Put back: mode MAP, range 10, CTR off, DATA off, heading reference NORM, MCP heading 0. Left as
they are: the IRS aligned and the route (below) active. The draw was traced offline from the WAT of the same build;
the fields were then checked live. Function numbers are WAT function indices of this build; offsets are decimal, from
the object's start. Screen captures: `PrintWindow` of the 3840x2160 captain's view, the EHSI cropped at 1750,1745
(370x385).

- **Build:** `BBS752.wasm` 29,765,919 bytes, SHA-256 `357292c9…45efc3d`, as plans 23 to 25 measured.
- **Not measured, because it needs a flight:** the ETA with a ground speed of 100 kt or more, the wind readout, track
  up, a waypoint being passed (what happens to leg 0), S/C, the offset path. Their sources are traced below; Task 9 and
  the owner's flight hear them.

## Step 1: the objects

- **The Captain's EHSI** is `bbs::nd`: export `nd_l_gauge_callback` = 23070 → `f960` (the gauges manager 0xf1334) →
  f23058(mgr, **6**) → `[0xf1364]` (live 0x2e9a70, vptr 79876, `nd+88` = 6). The First Officer's is index 7,
  `[0xf136c]` (`nd+88` = 7), export 23071. Vtable slot 3 = 23366 (setup), slot 4 = **23406 (draw)**.
- **What the nd keeps** (set in 23366, all checked live): +844 mcp, +848 tcas_computer, +852/+860/+864
  vor_control_panel, +856 dme, +868 adf_control_panel, +872 pfd, **+888 efis_control_panel**, **+892
  instr_source_selector_panel**, +896 hdg_ref_panel, +900 air_ground_sensing_system, +904 electrical, **+908 fms**
  (systems slot `[0xf1030]`), +920 bbos, +924 afds, +1216 the navigation database, **+1244 the route**.
- **The selected IRS:** `[instr_source_selector_panel+712]` (getter 44756; live a `bbs::irs`). The draw returns black
  when it is null.
- **The route** (+1244): `f39248(fms, 1)`, RTE 1's object in the map at fms+35216 (route number → route, the route at
  node+16; live 1 → 0x47a5a0, 2 → 0x612cd0). The side map fms+35196 (CDU side → route number) read {1 → 1, 2 → 1}.
  An event (23332) points +1244 at `f39168(fms, side)` again when the route changes.
- **The path object P** of a route: a static map at 1197208 (first node at +8; node: next, hash, key at +8, value at
  +12) from `i32 route+1700` (the route's id; live 10) to P. Not polymorphic. No entry while the route is empty.
- **Dark:** the shared gauge wrapper (23059) calls the draw only when `u8 nd+4` = 1 (powered), `u8 nd+5` = 0 (failed)
  and `u8 nd+64` = 0 (timed blank); otherwise the screen is filled black. The draw also blanks for 1 s when the
  instrument source changes (`u8 instr_source+748`, read and cleared). Live 1, 0, 0.

## Step 2: mode, range, heading and track

- **Mode:** `i32 efis+344` (getter 19961), copied from `L:B757_ADI_1_CTR_KNOB` while the panel is powered
  (`u8 efis+1036`). **0 VOR, 1 ILS (the panel's APP), 2 MAP, 3 PLAN**: each set live, memory and capture agreeing.
- **Centered:** `u8 efis+348` (getter 23342), toggled by each press of `L:B757_ADI_1_CTR_BUTTON`. Live 0 → 1 → 0;
  the capture showed the full rose around the aircraft.
- **Range:** `L:BBS_ND1_Range_Index` 0 to 5 → **10, 20, 40, 80, 160, 320** NM, as a number: `f64 nd+296` the selected
  range, `f64 nd+704` the drawn range, which follows 0.5 s later; `u8 nd+804` = 1 while it changes (the labels are
  hidden then). All six set live. The screen labels half the range on the track line (`setw(3)`: "5", "10", "20",
  "40", "80", "160"); centered MAP labels a quarter, ahead and behind.
- **IRS valid:** `u8 nd+805` (= `[irs+3428]` = 5 and `u8 irs+3040` and `[irs+3420]` = 4). `u8 nd+1272`
  (= `[irs+3420]` = 3). Live: 0, 0 with the IRS off; 1, 0 once aligned. With +805 = 0 the heading, track and map
  fields below are zeroed.
- **The number at the top:** `f32 nd+720`, drawn `round % 360`, three digits with leading zeros ("119"; 360 → "000").
  It is the heading when `i32 nd+756` = 0 and the track when 1 (+756 is a tablet option, `[bbos+596+4]`; live 0;
  forced 0 in VOR and ILS). Magnetic when `i32 nd+760` = 0, true when 1 (+760 = 1 when the IRS latitude is above 73°
  or below −60°, or `u8 hdg_ref+344` = 0, the switch on TRUE). Not valid and +1272 set: "---"; neither: nothing.
- **The words beside it** (drawn by 23425): `HDG` (+756 = 0) or `TRK`, only while +805; right of the number `M`
  (+760 = 0) or a boxed `TRU`. Live: "HDG 119 M", and "HDG 134 TRU" with the heading reference on TRUE (memory
  119.39 → 134.43, +760 0 → 1).
- **Its parts:** `f32 nd+712` heading (magnetic or true by +760), `f32 nd+716` true heading, `f32 nd+724` track,
  `f64 nd+744` true track; on the ground (`u8 air_ground+476`) the tracks are the headings. They come from simulator
  variables the nd registers (PLANE HEADING DEGREES TRUE and MAGNETIC, GPS GROUND MAGNETIC TRACK and TRUE TRACK,
  GROUND VELOCITY at `f64 nd+736`), so memory holds the values the screen used. Live 119.39, 134.43, 119.39, 134.43
  against the simulator's 119.39 magnetic and 134.43 true.
- **Heading bug:** `f64 mcp+848` (mcp = nd+844, getter 23572), degrees. Live: `L:BBS_AP_TARGET_HEADING` set to 147 →
  +848 = 147.0 and the bug on 147 in the capture; 0 → 0.0. Drawn only while +805, on the rose at (bug − nd+720); the
  dashed line from the aircraft to it in every mode but PLAN and centered VOR/ILS.
- **Failure flags** (23424, only while +805 = 0; amber, boxed): `HDG` (or `TRK`), and in MAP also `MAP`. Live with the
  IRS off: both in MAP, `HDG` alone in VOR, ILS and PLAN.

## Step 3: the top line, the route and the map

### The top line (23425)

- **No waypoint name is drawn.** Left: the distance and `NM`. Right: the ETA and `z`. Middle: the heading (above).
  Captures: "37.5NM   HDG 119 M   ------z".
- **Distance (MAP and PLAN):** to **leg 0** of the route (there is no active-leg index: the active waypoint is the
  first leg of the vector), from the aircraft's position (Step 4), by the haversine formula (4581): R = 6,371,000 m,
  result ÷ 1852 → NM. Under 100: one decimal in a width of 5 ("37.5"); 100 or more: the integer, truncated. `  ---`
  when the route has no leg, or leg 0's path terminator is 19 (VM) or 9 (FM). Live: 37.5 on screen; 37.48 computed
  from the same fields. While a modification is pending the distance is to the modification's leg 0 (`route+1624`,
  below).
- **In VOR and ILS** the left field is `DME` and the DME distance (`f64 nd+256` valid `u8 nd+264` in VOR; the dme
  object's station and `f64 dme+360` in ILS), `  ---` when invalid. Captures: "DME ---". Plan 28's.
- **ETA:** `------` when the ground speed is under 100 kt or there is no distance, so always on the ground (every
  capture). Otherwise the clock + distance ÷ ground speed: ground speed `f64 irs+3952` (kt), the clock the static
  `f64` at 1197328 (HHMM.mm, written each frame from `E:ZULU TIME`; live 1512.38 at 15:12.4Z), the sum rounded to the
  second, wrapped at 24 h, printed fixed with one decimal, width 6, zero filled ("1533.3"), then `z`. Not drawn in VOR
  or ILS while centered.

### The route (23448, the same in MAP and PLAN)

- **Fields of the route:** +668 the leg vector (begin, end, capacity; **1,632 bytes a leg**; getter 5175); +680 a
  second leg vector walked before it (always empty live; probably the legs flown); `u8 +1235` active (0 before
  ACTIVATE and EXEC, 1 after); `u8 +1620` a modification is pending and `+1624` → the modified route (live during an
  edit: 1 and a separate route object whose legs were the edited list; 0 and null after EXEC or ERASE); `f64 +640`
  the cruise altitude (23000).
- **Which routes are drawn:** the modification first when +1620 is set, then always the route itself. Line styles
  (captures): **active magenta solid, inactive cyan dashed, modification white dashed**. Every leg is walked; there
  is no cap on the count.
- **A leg:**
  - **+1608 the label drawn** (a UTF-32 `std::u32string`): "KOLM", "BTG", "TRAYL", "RW10L", "(800)", "(INTC)"; empty
    for a discontinuity. The narrow string at +0 holds the name only for airports and fixes (empty for navaids, whose
    name is at +816): read +1608.
  - **+72 where the position is** (switch in 44630): 0, 4, 7 → latitude `f64 +80`, longitude `f64 +88`; 1 and 2
    (navaids) → +832, +840 (2 can also use +880, +888, by a set at +784); 5, 6 → +1040, +1048; **3 → a route
    discontinuity** (position 0, 0). All in degrees.
  - **+24 the path terminator:** 0 none, 1 IF, 2 TF, 3 CF, 5 DF, 6 FA, 7 FC, 8 FD, 9 FM, 10 CA, 11 CD, 12 CI, 13 CR,
    14 RF, 15 AF, 16 VA, 17 VD, 18 VI, 19 VM, 20 VR, 21 PI, 22 HF, 23 HM, 24 HA. Live: 5 for a waypoint typed on
    LEGS; 1, 3, 3 for the ILS 10L's TRAYL, BLAZR, RW10L; 10 "(800)", 18 "(INTC)", 23 the hold at BTG.
  - Constraint and time (drawn only with DATA on, `u8 nd+209`): `i32 +372` the kind (0 none, 1 at, 2 at or above "A",
    3 at or below "B", 4 between, 5 above), `i32 +376` and `+380` feet; `f64 +1480` the ETA (HHMM + seconds ÷ 60;
    −1 none), drawn truncated to 4 digits and `Z`. Live: TRAYL 2, 2300 ("2300A" on LEGS); KOLM 1518.48 → "1518Z" under
    its name in the capture.
- **A discontinuity** is its own leg (+72 = 3, +24 = 0, no label). No symbol is drawn for it and the line is broken
  there. Live: KOLM, discontinuity, OLM, BTG, KPDX read as 5 legs, the second one the discontinuity; the captures
  showed no line across it.
- **Symbols:** nothing for terminator 0, 9 (FM) or 19 (VM). A four-point star for the rest, or a small circle when
  the terminator is 7, 8, 11 or 17 or the label starts with "(". **The active waypoint** (the first leg of +668, only
  while `route+1235`) is magenta with a magenta label, the others white (captures: KOLM magenta once the route was
  active, white while it was inactive). The label sits right of and below the symbol.
- **The line** follows P+2864 (a vector of points, longitude then latitude), broken at the indices in P+3468. Parked
  without a departure runway its first point was 0, 0, so a magenta line ran off the map toward 0° N 0° E; with
  runway 16L selected it starts at the runway.

### The VNAV points (green ring and green word, in 23448)

Each is drawn only if it is within range + 10 NM of the map's center.

- **T/C:** `u8 P+4000` set; latitude `f64 P+3856`, longitude `f64 P+3864`. Live with FL230 and the performance data
  entered: 47.1382, −122.6868, 24.0 NM from the aircraft at 220.6° true; the capture showed `T/C` there, on the leg
  to KOLM. (The data's "ZT/C" is two strings: `T/C` starts one character in.)
- **T/D:** `u8 P+4048` and `u8 P+4049` both set; longitude `f64 P+4032`, latitude `f64 P+4040`. Live 46.6855,
  −122.8291, 50.3 NM at 205.5°; `T/D` in the capture, past KOLM.
- **E/D:** `i32 P+3924` is a leg's index (−1 none); the point is that leg's position. Live 4 = RW10L.
- **S/C:** not stored: computed each frame (39681) from the legs' planned step altitudes (`i32 leg+1496`) or the
  step list at P+3792, only for the active route while `i32 fms+34980` reports a step. None live (no step on this
  route). Plan 26 reads it only if a field holds the result; none does, so S/C needs the same computation or stays
  unread until a flight shows one.
- Also green rings without a word at the points of P+4276 and P+4288 (captures: three along the descent; purpose not
  found), and an altitude or time marker (`u8 P+200`, `i32 P+204`, `f64 P+208`; off live).

### The other map items

- **Origin and destination airports:** `nd+1096` and `nd+1128` (a UTF-32 name, then longitude and latitude at +16
  and +24); a cyan ring and the name, always drawn in MAP and PLAN. Live "KSEA" 47.4499, −122.3118 and "KPDX"
  45.5887, −122.5969; both in the captures.
- **Runways:** `nd+968` (departure) and `nd+1032` (arrival): the runway with dashed centerlines and its name. Live:
  16L drawn at KSEA once selected.
- **The EFIS switches**, as bytes in the nd: `u8 nd+208` NAV AID, **`+209` DATA** (`L:B757_ADI_1_DATA_BUTTON` set live:
  0 → 1, and the capture gained "1518Z" under KOLM), `+210` ARPT, `+211` WPT, `u8 nd+304` TFC. Their items, TCAS,
  `TERR` (23427, `L:B757_ADI_1_TERR_BUTTON`) and the VOR and ILS pictures are plan 28's; the fix rings, the trend
  vector and the altitude range arc were measured in plan 28 but are outside its list (below).
- **Messages in MAP and PLAN:** `EXCESS` over `DATA` (amber, left, mid-height) when `u32 nd+1276` ≥ 100: a count of
  the background airports, stations and waypoints drawn this frame, zeroed each frame. Not the route. Live 0.
  `TCAS OFF` / `TCAS FAIL` (23460) are plan 28's.
- **Wind** (23426; bottom left; only while +805 and not in PLAN): `---` unless the ground speed is 100 kt or more and
  the wind is above 6 kt (a static byte at 1008842, cleared under 4 kt); then the speed (`AMBIENT WIND VELOCITY`,
  integer, width 3) and an arrow. Live `---`.
- **PLAN** adds a green arrow and `N` (right, lower half); it keeps the rose, the heading number and the heading bug
  on the rose, and draws no aircraft symbol.

### Where an item is on the screen

- **Projection** (23475 with 23582): the great-circle distance (spherical law of cosines, R = 6371 km, ÷ 1.852) and
  initial bearing from the map's center to the item; screen radius = distance ÷ range × (height × 0.6775); screen
  angle = bearing − rotation. Size `f32 nd+40` × `f32 nd+44` (live 704 × 859, so full range is 582 pixels).
- **Center and rotation, read rather than derived:** `f64 nd+1176` latitude, `f64 nd+1184` longitude,
  `f64 nd+1208` rotation, `f64 nd+1192` and `+1200` the center's screen point.
  - MAP: the aircraft (Step 4); rotation the true heading (the true track with track up); screen point (width ÷ 2,
    height × 0.8175), or (width ÷ 2, height × 0.596) centered. Live 47.4429, −122.3036, 134.43, (352, 702.2);
    centered (352, 512).
  - PLAN: north up (rotation 0); the center is a leg the CDU chooses (`i32 cdu+13800`, pushed into the nd by 40773);
    screen point (352, 411.2). Live: KOLM, leg 0, 46.9694, −122.9025.
- **What is drawn:**
  - Route waypoints are not tested for distance; they and everything else on the map are cut by a clip (23406, lines
    2123-2390): not centered, a **circle around (width ÷ 2, height × 0.8175) of radius height × 0.6775 − 66 pixels**
    (516 of 582, 88.7 % of the range), in PLAN as well, where the map's center is 291 pixels above the circle's;
    centered, the rectangle below y = height × 0.14. The screen's own edges cut too: not centered, 352 pixels either
    side (60.5 % of the range) and 157 below the aircraft (27 %).
  - The VNAV points, airports and background items also need distance ≤ range + 10 NM from the center.
  - Captures against the reading: MAP at range 10 and 40, KOLM (37.5 NM, 86° right (86.5°)) is not on the screen: 37.5 NM is
    beyond 60.5 % of 40 sideways; at 160 it is, with BTG and the approach at the right edge (102 to 113 NM, 50° to
    56° right), their labels cut by the edge. PLAN at 80 centered on KOLM: KSEA (37.6 NM at 41°) falls 528 pixels
    from the circle's center and is cut, as the capture shows (only a corner of its label); T/C 13.4 NM at 41° and
    T/D 17.3 NM at 170° are drawn where computed.

## Step 4: the position

- **For the top line's distance** the FMS reads `PLANE LATITUDE` and `PLANE LONGITUDE` (degrees) fresh on each call
  (39243; handles at fms+960 and fms+992); it keeps no copy.
- **For the map** the nd copies the selected IRS's position each frame: `f64 irs+3960` longitude, `f64 irs+3968`
  latitude → **`f64 nd+1160` longitude, `f64 nd+1168` latitude**.
- Live, parked: nd 47.442946, −122.303648; `PLANE LATITUDE` 47.4429463, `PLANE LONGITUDE` −122.3036476: the same to
  the sixth decimal. In a turn and in flight: not measured (the aircraft never moved); Task 9 compares the two again
  on the owner's flight. If they stay equal the reader computes the distance from nd+1168 and nd+1160 and needs no
  simulator input; if the IRS position drifts from the simulator's, the top line needs `PLANE LATITUDE` and
  `PLANE LONGITUDE` as inputs, since that is what the draw takes.

## In flight, read only (the owner's own flight, 2026-11-11 about 17:57Z)

The owner was in cruise (FL380 over the Gulf of Mexico, heading 320, MSFSBA running) while the plan's code was under
review; memory was read, nothing was written and nothing installed.

- **The position:** the nd's copy read 24.919594, −90.172232, then the simulator's `PLANE LATITUDE` and
  `PLANE LONGITUDE` 24.920356, −90.172817, then the nd 24.920966, −90.173287 (about 0.1 NM a second at 402 kt): the
  simulator's value lies between the two memory reads, so the IRS copy is the simulator's position in flight too.
- **Ground speed:** `f64 irs+3952` 401.75 against `GROUND VELOCITY` 401.69: knots.
- **Passed legs move to the flown vector:** `route+680` held 4 legs (ENEBA, NOSUG, OMVIP, KEHLI) and `route+668` began
  with KELPP, the leg flown to: leg 0 of +668 is the active waypoint in flight, and its index in the walk is the flown
  count.
- **The top line computed from the fields:** KELPP 167.0 NM, the clock 1757.15, 401.75 kt → `1822.1z` by the traced
  formula. No capture was taken (the owner's view); Task 9 compares the row with the screen.
- **The path registry** still held four entries (ids 12, 11, 10, 9) hours after the session's edits: stale entries
  stay, and the list had not grown in flight.
- **Wind:** the show flag (1008842) read 1 with 70 kt from 287°: the readout is drawn in cruise (plan 28's).
- T/C's flag was 0 past the top of climb, T/D's flags 0 with the descent not yet computed on this route, E/D −1.

## The route flown through the states

| State | Legs read from memory | Screen |
|---|---|---|
| No route | 0 legs | `---NM`, `------z` |
| RTE 1 entered, inactive | KOLM, discontinuity, OLM (0.17° N 78.09° W: the wrong OLM), BTG, KPDX; +1235 = 0 | cyan dashed, all names white, 37.5 NM |
| Activated | the same; +1235 = 1 | magenta, KOLM magenta |
| BTG moved up over the discontinuity (MOD) | +1620 = 1, +1624 → KOLM, BTG, KPDX | the white dashed KOLM to BTG beside the magenta route |
| Executed | KOLM, BTG, KPDX; +1620 = 0 | magenta |
| 16L, ILS 10L with its missed approach | KOLM, BTG, TRAYL, BLAZR, RW10L, (800), (INTC), BTG, BTG (hold) | T/C, T/D, the runway, the approach |

## For the recipe (Task 3)

1. Export `nd_l_gauge_callback` = 23070 → `f960` → f23058(mgr, 6) → `[0xf1364]`; the draw = vtable slot 4 = 23406
   (vtable 79868, vptr 79876); the setup = slot 3 = 23366.
2. 23366 stores the pointers: efis +888, instr source +892, hdg_ref +896, air_ground +900, fms +908, mcp +844, and
   `f39248(fms, 1)` → +1244 (the last store).
3. Getters: 19961 → efis+344 (mode); 23342 → efis+348 (centered); 44756 → instr_source+712 (the IRS); 5150 →
   irs+3960 (16 bytes: longitude, latitude); 44812 → irs+3904 (ground speed at +48); 23572 → mcp+848; 8585 → nd+88.
4. nd fields written by the draw and its helpers: +296/+704/+804 (23423); +712, +716, +720, +724, +744, +756, +760,
   +805, +1272 (23406); +1160/+1168 (23412); +1176, +1184, +1192, +1200, +1208 (23472, 23473, 40773); +1276
   (23406, 23430, 23442); +208 to +211 and +304 (the efis update, through `[efis+1012]`).
5. Route: 5175 → +668; 20485 → +680; 15136 and 23483 → the 1,632 stride; 14739 → +1235; 14740 → +1620; 23447 →
   +1624; 37385 → the P lookup by +1700 (74007 → 1197208).
6. Leg: 44630, the switch on +72 (and 5181); direct loads in 23448 for +24, +372, +376, +380, +1480, +1608.
7. P: 24530 → +2864; 24564 → +3468; 24152 → +3976 (flag at +24 = +4000); 24573 → +3848 (T/C: flag, latitude,
   longitude); 24153 → +4008 (T/D: longitude +24, latitude +32, flags +40 and +41); 24531 → +3924; 24546 → +3456.
8. The top line: 41110 (the distance: 39168, 44630, 4581 with 6.371e6 and 1852); 74319 → the clock at 1197328;
   74321 (the ETA sum).
9. Words: `HDG`, `TRK`, `TRU`, `NM`, `DME`, `  ---`, `------`, `      z` in 23425; `MAP` and the flags in 23424;
   `EXCESS`, `DATA` in 23406; `T/C`, `T/D`, `E/D`, `S/C` in 23448; the constants 0.6775 (487436) and 0.8175 (487432).

# Plan 28: VOR and ILS, TCAS, the map options and the rest of the map (plan 28, Task 1)

Measured 2026-11-12, about 10:10 to 11:45Z, MSFS 2024, the same 757 parked at a KSEA gate (47.44357, −122.30393,
heading 010 M, 025.3 true), BBS Nest running, MSFSBA closed, AI traffic from SayIntentions. Build as above
(`BBS752.wasm` 29,765,919 bytes, SHA-256 `357292c9…45efc3d`). Memory was read only; no snapshot was kept. Writes to the
simulator, at the owner's word ("Please do whatever you like with this aircraft"), each put back: the Captain's EFIS
mode and range, CTR and TFC (pulses), NAV AID, APRT, WPT, WXR and TERR, the TCAS mode knob
(`L:B757_PED_TCAS_MODE_KNOB`), the VOR L manual frequency (`L:B757_VOR_L_FREQ_WHOLE_INC` / `_FRAC_INC` steps, 117.40
→ 116.80 → 117.40), `L:BBS_VOR_L_COURSE` (130 → 187 → 130), the ILS panel (PARK → 110.30 → PARK), `L:BBS_ILS_COURSE`
(215 → 164 → 215), and VOR L autotune (`L:B757_MCP_VORDME_1_BUTTON` pulsed: the switch to VOR mode had turned autotune
off and it did not come back by itself in MAP). Left as they are: a route on the Captain's CDU (KSEA RW16L, KOLM,
TRAYL, BLAZR, RW10L, ILS 10L with its missed approach, active; PERF INIT ZFW 140.0, reserves 4.0, CI 50, FL230), FIX
INFO 1 = TCM with radial 300 and a 15 NM circle, and PRED ETA-ALT 10000. The draw was traced offline from the WAT of
the same build; the fields were then checked live. Function numbers, offsets and captures as in plan 26; W × H below
is the screen size `f32 nd+40` × `f32 nd+44` = 704 × 859.

- **Colors:** fill paints (f8962) 1171040 cyan, 1171048 red, 1171056 amber, 1171064 white, 1171072 green, 1171080
  magenta, 1171088 black; stroke paints of width 3 (f22941) 1171096 cyan, 1171104 red, 1171112 amber, 1171120 white,
  1171128 green, 1171136 magenta; base colors 1171012 cyan (60, 240, 240) to 1171036 black, initialized in 69511 to
  69540. Live ARGB (`[[static]+4]+12` for fills, `[[static]]+0` for pens): amber `ffffe600`, green `ff00ff00`, magenta
  `ffff00ff` (1171080 and 1171136; the initializer of 1171136, 69542, was not decoded), cyan `ff3cf0f0`.
- **Full and expanded:** "full" is centered (`u8 efis+348` = 1), "expanded" not. One dot d = W × 0.0725 = 51.04 px.

## Step 1: VOR and ILS

- **Draw order** (the end of 23406, mode `fr+284` = f19961(efis)): ILS (1) 23455 (center), 23341(nd, center,
  `f64 nd+232`), 23456, 23365; VOR (0) 23455, 23341(nd, center, `f64 nd+224`), 23365. Then in every mode 23457, (MAP:
  23458), 23459, 23460 (TCAS), and 23461 only while `u8 nd+805`. No map items in VOR or ILS: 23406's map block runs
  only in MAP with +805, or in PLAN. From the code.
- **Geometry:** center (23455) x = W ÷ 2, y = H × 0.455 (488156) full VOR/ILS, H × 0.596 (488160) centered MAP,
  H × 0.8175 (487432) expanded; live (352, 390.8) full, (352, 702.3) expanded. Rose radius (23343) H × 0.275 (488164)
  full VOR/ILS = 236.2 px, H × 0.325 (488168) centered MAP, H × 0.6775 (487436) expanded = 582 px.

### Sources

- **VOR course** `f64 nd+224`: written each frame by 23412 **only in VOR mode** (and only with `nd+852`, the VOR
  panel, and `nd+1216`, the navigation database, non-null): `f64 vcp+784` (getter 23466) wrapped to [0, 360) by
  fmod(x + 360, 360), 0 if not finite. `vcp` = `nd+852`, the Captain's `vor_control_panel` (system 63, "L"; the FO's
  nd gets 64). +784 is copied each update (47472) from the L:var at handle `vcp+520` = **`L:BBS_VOR_L_COURSE`**
  (static 1038328; FO `L:BBS_VOR_R_COURSE`, 1038400); 47468 also writes it. With `i32 nd+760` = 1 (TRU) the course is
  shifted by remainder(f4257(nd+448) − f4257(nd+480)), the true minus the magnetic heading; VOR only. Live: +784
  130.0; `nd+224` 0.0 in MAP (23412 does not run), 130.0 in VOR.
- **ILS course** `f64 nd+232`: written by the ILS receiver L (`bbs::ils_receiver`, system 95, its +356 = the
  Captain's nd; R = system 97 writes the FO's nd; C = 96 writes none) in 44743 through f23432(nd) = nd+208, field +24:
  `f44726(ils_control_panel)` = **`L:BBS_ILS_COURSE`** (static 1039120, read by f4133). Written every update, tuned or
  not. Live 215.0, then 164 when set.
- **The ILS signal:** S = `[ils_receiver+364] + 448` (inside `bbs::b757_super` 0x2e89d0, at +1032; all three
  receivers share it), N = `[S]` the localizer's navaid record.
- **No course number is printed** in either mode: no function in 23406's call tree references a CRS word or formats
  nd+224 or +232 (from the code).

### The course pointer (23341)

Rotated by the course − `f32 nd+720` (the heading number), the frame translated to the center. From the code; the
captures agree (expanded VOR and ILS, full VOR).

- **Expanded VOR, expanded ILS, full VOR** (one shape, fixed pixel sizes): a white filled head (pen 1171120)
  (−0.5, −120), (−7, −110), (−7, −90), (7, −90), (7, −110), (0.5, −120) px; a magenta line (1171136) from y = −127.5 to
  −580 expanded, to −(radius − 10) full; a white filled tail rect (−7, 67.5, 14 × 20); a magenta dashed tail of 21
  dashes of 15 px from y = 90 every 23.5 px (gap 8.5), behind the center (full: it stops at radius − 10).
- **Full ILS:** a different white shape scaled by H ÷ 1000 (f23345, f23346; white fill 1171120 over black 1171088):
  the head polygon (15 points, x ±4 to ±24, y 0 to 112 units) at −(radius − 20 H ÷ 1000), the tail polygon (x ±9,
  y 0 to 112) 112 H ÷ 1000 further back. No magenta line, no dashed tail.

### The deviation scale and the glideslope

- **Dots:** 4 circles (oval f23344, W × 0.02 = 14.1 px) on the axis across the course, rect left edges −2d, −d,
  +d − 7, +2d − 7 (tops at y = 0): **two each side, 1 dot = d = 51 px**. White (1171120); in ILS amber (1171112) while
  `u8 nd+274`. VOR: always drawn. ILS: only while `u8 nd+273`. Live: the four dots in VOR with no station.
- **Bar:** a magenta (1171136) rect W × 0.02 wide; expanded and full VOR from y = −0.0975 H to +0.065 H (139.6 px);
  full ILS ±(radius − 0.142 H) (±114 px). x offset = **value × 2d**. VOR `f64 nd+248`, drawn while `u8 nd+265`; ILS
  `f64 nd+240`, drawn while `u8 nd+272`.
- **The sign (live): a positive value is a bar right of the course, looking along the course, so fly right; dots =
  value × 2; pinned at ±1.0 (the second dot).**
- **VOR deviation (23412):** radial = the magnetic bearing from the station to the aircraft (f4582(station+40, the
  aircraft's position f4042(navdb), magvar), magvar the station's declination cached at `f64 nd+880` for the station
  at `nd+876`, f69687); x = remainder(course − radial, 360), folded into ±90 (x > 90 → 180 − x, x < −90 → −180 − x);
  **`nd+248` = clamp(x ÷ 10, −1, 1)**: 5° a dot. `nd+265` = 1.
  - Live: VOR L on 116.80 (SEA, type 2), course 130: `nd+265` 1, `nd+248` 1.000 (pinned). Course 187: 0.255; the
    expanded capture showed the bar about half a dot (0.51) from the center on the right of the course arrow, looking
    along the course (the aircraft heading 010, the arrow pointing down the screen). The aircraft is on SEA's 010
    radial; course 187 TO follows the 007 radial, west of the aircraft, so looking south the line is on the right:
    agrees.
- **ILS localizer (44743, receiver L):** deviation `f64 S+8` (degrees; also written to `L:BBS_LOC_DEV_DEG`), its sign
  reversed when |the aircraft heading (`f64 [f4042(b757_super)]+24`) − (BBS_ILS_COURSE + N+104 − N+56)| > 90 (back
  course); width w = `f64 N+112` (≤ 0 → 5°). **`nd+240` = clamp(−dev ÷ (w ÷ 2), −1, 1)**: 1 dot = w ÷ 4 (1.25° for
  5°), pinned at 2 dots. The PFD gets clamp(−dev ÷ (w ÷ 4), ±2.2).
  - Live on 110.30 (ISNQ, received on the ramp): `nd+272` 1, `nd+240` 1.000 (pinned), `S+8` 7.44, `u8 S+80` 1,
    `u8 S+81` 1; the capture showed the bar at the second dot right of the course arrow.
- **Glideslope (23456; ILS mode only; needs the pfd at `nd+872`):** the PFD's ILS block g = pfd+136 (getter 23560),
  written by receiver L through its +352: **`f64 g+16` = clamp(−(`f64 S+56`) ÷ 0.35, −2.2, 2.2)**, in dots (0.35° a
  unit), when `u8 N+144` (the navaid has a glideslope) and `u8 S+81` (received); `u8 g+35` the pointer valid.
  **Positive = the pointer up.** The scale is drawn only while `u8 g+33` (set while an ILS frequency is tuned, cleared
  on failure); the pointer only while `u8 g+35` and the value is finite.
  - Scale (f74269): x = W × 0.908 (639 px); center cy = H × 0.72 (618.5) expanded, the rose center (H × 0.455) full;
    4 dots (oval W × 0.02) at rect tops cy − 2.175d + {0, d, 3.35d, 4.35d}, about ±1.175d and ±2.175d from cy; a
    filled center marker rect (0, cy, 25 × 10 px). White; amber while `u8 g+39` (test).
  - Pointer: a magenta (1171136) triangle 14 × 28.4 rotated 90°, at cy − value × 1.0875d (2 units = the outer dot).
  - Live: on 110.30 `S+56` −2.088, `g+16` 2.200 (pinned), `g+35` 1; the capture showed the scale at the right with the
    magenta pointer at its top (the upper outer dot). Tuned 110.50 and 110.35 (no station): `g+33` 1, `g+35` 0.
  - No glideslope picture in VOR mode.

### TO/FROM (VOR)

- `i32 nd+268` (23412, VOR mode, valid station): |remainder(course − radial)| > 90 → **+1 = TO**, < 90 → **−1 =
  FROM**, 90 within 1e−6 → 0; zeroed each frame first.
- **Text** (23365, VOR mode, full and expanded, while ≠ 0): `TO` / `FROM`, white (1171064), font `nd+336`, at
  (W × 0.835, H × 0.895) = (587.8, 768.8).
- **Triangle** (23341, full VOR only, while ≠ 0): white filled (0, ∓62), (−10, ∓44), (10, ∓44) in the course frame,
  ahead for TO, behind for FROM.
- Live: +1 with SEA tuned, course 187; `TO` white bottom right expanded and full, and the TO triangle in full VOR; no
  TO/FROM with no station (`nd+268` 0).

### The labels and the ILS frequency (23365)

- **Bottom left**, (W × 0.048, H × 0.895 + 31) = (33.8, 799.8), green (1171072), font `nd+336`: VOR mode `VOR L` when
  f8585(nd) (`nd+88`) = 6 (Captain), else `VOR R`; ILS mode `ILS`. There is no L/R selection: the label follows the
  display's side (the Captain's EHSI always uses VOR L, `nd+852`). Live `VOR L`, `ILS`.
- **Bottom right**, (W × 0.835, H × 0.895 + 31) = (587.8, 799.8), green, **ILS mode only**: the ILS frequency
  `f64 nd+280`, `fixed`, `setprecision(2)` ("110.30"), when `nd+280` ≠ −1 AND the receiver the instrument source
  selects (`[instr_source+736]`, getter 44760; live receiver L 0x2d4350) has `u8 S+80` (the localizer received,
  through 44737 → `[r+364]+448`); otherwise `------`.
  - `nd+280` (44743): −1 when the panel has no frequency (`i32 ils_panel+364` = −1, f23192), else `i32 +364` +
    `i32 +360` × 0.01 (f23193). The panel `[receiver+348]` = `bbs::ils_control_panel` 0x2c9250; its knobs
    `L:B757_ILS_FREQ_WHOLE_INC` / `_FRAC_INC` (handles 1039264 / 1039192, 44731; the fraction knob is ignored while no
    frequency is set).
  - Live: parked `nd+280` −1 and `------`; 110.50 and 110.35 (no station) `nd+280` = the frequency, `nd+272` 0;
    110.30 with ISNQ `110.30` in the capture (`L:BBS_757_ILS_FREQ` read 110.3).
- **No VOR frequency or ident is drawn in VOR mode** (only `VOR L` and TO/FROM).

### DME (23425, the top line's left field in VOR and ILS)

- `DME` white (1171064), font `nd+336`, at (W × 0.108 − 40, H × 0.081) = (36, 69.6); the number white, font `nd+312`,
  at (W × 0.108 − 16.5, H × 0.081 − 7.5) = (59.5, 62.1). MAP's `NM` is not drawn.
- **VOR mode:** valid `u8 nd+264`, distance `f64 nd+256` (23412), only while the VOR is valid (below), the station is
  DME-capable (type byte `station+32` in 1 to 8, f23469) and the distance from f47465 (f69679) is finite and ≥ 0.
- **ILS mode:** the Captain's `bbs::dme` (`nd+856`, system 85): valid when not failed (`u8 dme+5` 0 and `u8 dme+4` 1,
  f73997), station `[dme+352]` (getter 32466) non-null with **type byte 5**, and `f64 dme+360` (32467) finite, ≥ 0.
- **Format:** under 100 `fixed`, `setprecision(1)`, `setfill(' ')`, `setw(5)` (" 12.3"); 100 or more `" "` and the
  integer, truncated; invalid `  ---`.
- Live: VOR with SEA `nd+264` 1, `nd+256` 0.54 → `DME 0.5`; ILS on 110.30: the DME's station ISNQ, type 5,
  `dme+360` 0.56 → `DME 0.6`. **The DME follows the ILS in ILS mode** and went back to SEA in MAP. No station:
  `DME ---`.

### The flags and the no-signal cases (23412, 23459, 44742 to 44746)

- **VOR valid** (23412) needs: VOR mode, the panel and the navigation database; the station f47466(vcp) =
  `[vcp+368]`, null unless the panel's circuit is available (f36730(electrical vcp+344, &vcp+736), id 1 Captain,
  145 FO) and the panel's EFIS (`vcp+352`) is not in ILS mode; the station's frequency (`i16 station+16`, MHz × 100)
  equal to the panel's **manual** frequency `i32 vcp+780` × 100 + `i32 vcp+776`; f47465 valid (frequency valid,
  station VOR/DME-capable, in reception per f69679); the bearing computable. Otherwise: no bar (`nd+265` 0), no
  TO/FROM, no DME (`nd+264` 0); **the dots and the course pointer stay; no flag is drawn for "no signal"**. Live:
  manual 117.40 (no such station) → `nd+265`, `+268`, `+264` 0; the capture showed `DME ---`, the course pointer and
  the four dots, no bar, `VOR L`, no TO/FROM.
- **Tuning:** the VOR panel tunes only VOR channels (f39598: 108.00 to 117.95 by 0.05, below 112.00 even tenths
  only). In MAP and PLAN the FMS autotunes (`u8 vcp+740` = 1, toggled by `L:B757_MCP_VORDME_1_BUTTON`; light
  `L:B757_MCP_VOR1_MODE_AUTO`): station +368, its frequency +768 / +772. In VOR and ILS mode +740 is forced 0 and the
  manual frequency +776 / +780 (knobs `L:B757_VOR_L_FREQ_WHOLE_INC` / `_FRAC_INC`, vcp+592 / +664) is tuned (47474).
  Live: the switch to VOR mode set +740 to 0 and the station to null; back in MAP +740 stayed 0 until the button was
  pressed.
- **`VOR` flag** (23459, from the code): VOR mode, when f36730(electrical `nd+904`, 1 Captain or 145 FO) is false
  (the VOR receiver's circuit, the same id as `vcp+736`). Amber `VOR` (1171056, font `nd+344`) at (W ÷ 2 − 27,
  H × 0.9) = (325, 773.1) in an amber box (pen 1171112) (W ÷ 2 − 31, H × 0.9 − 4, W × 0.089, H × 0.046).
- **`LOC` flag** (23459, from the code): ILS mode while `u8 nd+288`; text and box as `VOR`.
- **`G/S` flag** (23459, from the code): ILS mode while `u8 nd+289`: amber `G`, `/`, `S` stacked at x = W × 0.9,
  y = gsCy − 50 + {2.5, 33.5, 68.5} (gsCy the glideslope scale's center), in an amber box (W × 0.9, gsCy − 51,
  W × 0.0373, H × 0.1218).
- **ILS states** (receiver L writes through nd+208: +64 → `nd+272`, +65 → +273, +66 → +274, +80 → +288, +81 → +289):
  - Normal (44743; 44746 not failed, not testing): +273 = 1 (dots); +272 = 1 only while a frequency is set AND the
    localizer is received (`u8 S+80`), else 0 (no bar); +288 / +289 = 0. **Losing the signal draws no flag**: only the
    bar and the glideslope pointer go. Live: parked +273 1, +272 0; no station +272 0; ISNQ +272 1.
  - No ILS frequency: `ILS`, `------`, the dots, no bar, no glideslope scale (`g+33` 0), no flags. Live parked.
  - Receiver unpowered or failed (44746, from the code: f36730(electrical, &r+380) false; ids live L 127, C 2, R 143):
    +272 / +273 0 (no bar, no dots); +288 and +289 = 1 **if an ILS frequency is set** (`LOC` and `G/S`), else 0; the
    PFD's `g+32` to `+35` 0 (no glideslope scale).
  - Self-test (from the code: `u8 r+384` set; 44742, time since `i64 r+392`): 0 to 3 s `LOC` and `G/S`, no dots, the
    bar at −1; 3 to 5 s dots, the bar pinned, flags off; 5 to 8 s amber dots (+274 1), the bar sweeping −1 → −0.5 and
    the glideslope 2 → 1; 8 to 11 s the bar −0.5 → 0.5; then `r+384` 0, +274 0. Live: not testing.

### What the header does in VOR and ILS (23424, 23425, 23426, 23482)

- **Heading number, `HDG`/`TRK`, `M`/`TRU`:** drawn in every mode; `nd+756` (track up) is forced 0 in VOR and ILS by
  23416, so always `HDG` there.
- **Left field:** `DME` and its distance instead of MAP's distance and `NM`.
- **ETA:** drawn in expanded VOR and ILS (live `------z` in expanded ILS), **not in full** (live: none in full VOR).
- **Wind** (23426): while `u8 nd+805` in every mode but PLAN, so in VOR and ILS too, bottom left above `VOR L`/`ILS`
  (captures `---`).
- **Range label:** expanded VOR and ILS draw MAP's half-range label (23482 with p5 = 0: `setw(3)`, (int)(range ÷ 2),
  white, hidden while `u8 nd+804`; captures "5" at range 10). **Full VOR and ILS draw no range label:** 23482 with
  p5 = 1 draws a white line from the rose top to H × 0.14 and, while +805 and the track valid, a white track triangle
  rotated by (`nd+724` track − heading) at radius − 66. The centered aircraft symbol is 23480 (white), not MAP's
  triangle.
- **Failure flags** (23424, +805 = 0): `HDG` only (plan 26).
- **TERR** (23417): allowed in expanded VOR and ILS (not centered, not PLAN).
- **TCAS messages:** `TCAS OFF` in expanded VOR and ILS, not in full (Step 2; live).
- The map center and rotation (23416 → 23472) are computed in VOR and ILS too (rotation the true heading); 23457
  uses them.

### Full against expanded

- **Full VOR:** the full rose (radius H × 0.275 around H × 0.455); the same pointer, bar and dots as expanded, the
  line and dashed tail stopping at radius − 10; the TO/FROM triangle; `TO`/`FROM`, `DME`, `VOR L`; no ETA, no range
  label, no `TCAS OFF`, no heading bug line; the bearing pointers unclipped. Live (capture).
- **Full ILS:** the full rose; the scaled white pointer with no magenta line; the bar ±(radius − 0.142 H); the
  glideslope scale centered on the rose center; otherwise as full VOR. From the code.
- **Expanded:** the arc around H × 0.8175, the pointer line to −580 px, the glideslope scale at H × 0.72, the ETA,
  the half-range label, the heading bug line, `TCAS OFF`; the bearing pointers clipped above the aircraft. Live.

### The bearing pointers (23457)

- All modes but PLAN, while `u8 nd+805`; two green pointers (fill 1171128; the second has a black fill 1171088 inside
  its double outline).
- **Selectors:** `L:BBS_DDRMI_LEFT` / `L:BBS_DDRMI_RIGHT` (handles 1112416 / 1112488; FO `_FO` 1112776 / 1112848),
  through f23561: **0 → VOR** (pointer 0 `nd+860` VOR L, pointer 1 `nd+864` VOR R; station f47467: `[vcp+368]`, or in
  ILS mode `[vcp+372]` if its frequency matches the manual one; needs a VOR-capable type and f47465 valid), **1 →
  ADF** (`nd+868` adf_control_panel, f23565 ADF L, f23566 ADF R, position at +24), anything else none.
- **Angle** = the true bearing from the aircraft (`nd+1160`) to the station − `f64 nd+1208` (the rotation). Pointer 0
  a single-line arrow (head at radius − 14 / 30 px), pointer 1 a double-line arrow; the head toward the station, the
  tail opposite. Expanded: clipped to the area above the aircraft (rect 0, 0, W, center y).
- Live: both selectors 0 (VOR), VOR L's station SEA, VOR R none; the full VOR capture showed a green pointer toward
  SEA (about 190 on the rose).

### The heading bug line (23461)

- `mcp` = `nd+844`, `f64 mcp+848` (23572): two magenta squares (W × 0.035) on the rose radius at (bug − heading);
  then the **magenta dashed line** (10-px dashes every 50 px from the center, 11 dashes, 5 in centered MAP) **in MAP
  and expanded VOR/ILS; not in PLAN nor full VOR/ILS**. Live: the full VOR capture showed the bug.

## Step 2: TCAS

- **The object:** `tcas = [nd+848]` (stored by 23366; the static slot `[0xf12c8]`), class `bbs::tcas_computer` (vptr
  121192; live 0x2e66e0). Its pointers (set in 46510, checked live by class name): +748 irs, +752 adc, +756 ra (radio
  altimeter), +760 electrical, +764 the selected `xpndr` (f48869(xpndr_control_panel)), +768 / +772 the two pfds,
  +776 warn_elex, +780 air_ground_sensing_system, +784 eicas_computer, +788 xpndr_control_panel, +792
  instr_source_selector_panel. 23460 draws nothing at all when `[nd+848]` = 0.

### The messages (23460, called unconditionally near the end of 23406, every mode)

- **The left block:** translate (W × 0.041, y0), **y0 = H × 0.433 expanded, H × 0.199 centered**: (28.9, 371.9) or
  (28.9, 170.9). Line 1 at (0, 0), line 2 at (0, 30). Font `nd+336` (the font of `EXCESS`/`DATA`).
- **`u8 nd+1273`** (TCAS messages allowed in this mode), written each frame by 23416 (called at 23406's line 1228):
  reset to 0, then by `i32 efis+344`: **MAP 1; VOR and ILS 1 when not centered; PLAN stays 0**; any other value 1.
  Live: 1 in MAP and expanded VOR/ILS, 0 in full VOR and full ILS. PLAN from the code.

| Item | Condition | Line | Color |
|---|---|---|---|
| `     TEST` (5 spaces) | `u8 tcas+891` (testing, 46537); **not** gated by +1273, mode or IRS | 1 | white (1171064) |
| `TCAS` / `FAIL` | f73997 (`u8 tcas+5`, or not `u8 tcas+4`) and `u8 nd+1273`; nothing below | 1 / 2 | amber (1171056) |
| `TFC` | `i32 tcas+800` (getter 46534) = 1 or 2, AND `u8 nd+304` (TFC) AND `u8 nd+1273` | 1 | green (1171072) |
| `TA ONLY` | +800 = 1; **not** gated by +1273 (drawn in PLAN and full VOR/ILS too) | 2 | green |
| `TCAS` / `OFF` | +800 = 0 (or anything but 1, 2) AND `u8 nd+1273` | 1 / 2 | white (1171064) |

- FAIL excludes TFC, TA ONLY and TCAS OFF; TA ONLY and TCAS OFF exclude each other; TFC goes with TA ONLY or alone
  (TA/RA). TEST overlays line 1 of whichever is drawn ("TFC  TEST", "TCAS TEST" over OFF or FAIL).
- **`TRAFFIC`:** after the restore, when (`u8 tcas+889` a TA present OR `u8 tcas+890` an RA present) AND
  `u8 nd+805`; any mode (no mode or +1273 test). At **(W × 0.7768, H × 0.38)** = (546.9, 326.4), right of center,
  a bit above mid-height; font `nd+312`; **red (1171048) with +890, else amber (1171056)**; length passed 8. During
  the self-test +890 = 1, so `TRAFFIC` is red.
- No other TCAS text exists in 23460 (no OFFSCALE, no ABOVE/BELOW, no no-bearing line), and none in the top or bottom
  corners.
- **Live:** knob 0, 1, 2: +800 = 0, white `TCAS` over `OFF` (capture). Knob 3 and 4: +800 = 1 on the ground,
  `i32 tcas+796` = 1, green `TA ONLY`. TFC pressed (`u8 nd+304` = 1): green `TFC` over `TA ONLY`. A TA in the list:
  `u8 tcas+889` = 1, amber `TRAFFIC` right, mid-height. Captures: no `TCAS OFF` in full VOR, `TCAS OFF` in expanded
  VOR/ILS.

### The list (`tcas+736`)

- A `std::vector` (getter 46509 → tcas+736; begin +736, end +740, capacity +744), **40-byte stride** (iterator ++ in
  23553 adds 40); the builder stops at **30 entries**. Entry (built in 46549 at its frame +32; constructor 23554 sets
  +16 = −1):

| Offset | Type | Meaning |
|---|---|---|
| +0 | i32 | kind: 0 other, 1 proximate, 2 TA, 3 RA |
| +4 | i32 | trend: 0 none, 1 climbing (intruder VS ≥ +500 ft/min), 2 descending (VS ≤ −500) |
| +8 | f64 | relative altitude, feet = intruder PRESSURE ALTITUDE − own altitude (`f64 [adc]+40`) |
| +16 | f64 | always −1 (unused) |
| +24 | f64 | intruder longitude, degrees |
| +32 | f64 | intruder latitude, degrees |

- Every entry has a position; there is no no-bearing case.
- Live: the vector is never allocated at knob 0 to 2 (begin = end = capacity = 0). With the knob at 3 or 4 it filled
  within 2 s with AI traffic, 3 to 5 entries: e.g. kind 2, trend 0, dz 5 ft, 47.44413, −122.30795; kind 1, trend 1,
  dz −134; kind 0, trend 0, dz 1060. **Entries change every read** (positions, dz); **the order is not by distance.**

### Kinds, shapes and the tag (23552, at the projected point p, symbol size s = 25 px)

| kind | Shape | Color | Needs TFC or the test |
|---|---|---|---|
| 0 other | hollow diamond, 22.5 wide × 25 high (23519, pen 1171120) | white | yes |
| 1 proximate | filled diamond, the same size (23359, white brush) | white | yes |
| 2 TA | filled circle, radius 12.5 (23404) | amber (1171056, pen 1171112) | no, always |
| 3 RA | filled square 25 × 25 centered on p (74278) | red (1171048, pen 1171104) | no, always |

- With the show flag off, kinds 0 and 1 draw nothing at all (no tag, no arrow). Live: with TFC off only the TA was
  drawn.
- **Trend arrow:** at p + (25, 0), right of the symbol: a vertical line (0, −12.5) to (0, 12.5), the head at the
  bottom (−5.25, 7.25), (0, 12.5), (5.25, 7.25); trend 2 as is (**down, descending**); trend 1 rotated 180° (**up,
  climbing**); trend 0 none. The symbol's color.
- **The tag:** `+` when +8 ≥ 0 (so 0 is `+`), `-` otherwise; then `setw(2)`, `setfill('0')`, trunc(|+8| ÷ 100)
  (hundreds of feet, toward zero): `+10`, `-02`, `+00`, `-00`, `+27`. At (p.x − 10.5, p.y − 47.5) above the symbol
  when ≥ 0, (p.x − 10.5, p.y + 17.5) below when negative. Font `nd+312`, the symbol's color.
- **Live (capture against memory, range 20, each place computed from the nd's center and rotation 25.34):** kind 0,
  dz 739 ft, 8.73 NM, 37.6° left → hollow white diamond `+07`; kind 1, dz 1065, trend 1, 3.76 NM, 10.9° left →
  filled white diamond `+10`, up arrow; kind 1, dz 1110, trend 1, 1.29 NM, 162° right → filled diamond `+11`, up
  arrow; kind 0, dz 2525, trend 2, 7.15 NM, 112° right → hollow diamond `+25`, down arrow. All four where computed. A
  TA 0.2 NM away (a taxiing aircraft): an amber filled circle with `+00` above it and an up arrow. A later capture
  showed a negative tag (`-03`) below its symbol.

### Placement and clip

- 23452 is called from 23406 (line 3741) only inside the map block (`[nd+908]` fms and the navigation database) and
  only when `u8 [fr+283]` = **MAP (2) AND `u8 nd+805`**: **MAP only, centered or not; never VOR, ILS or PLAN**.
- The entry's (+24 longitude, +32 latitude) goes through **23475 (with 23582), the projection of the map items**:
  center `f64 nd+1176` / `+1184`, rotation `f64 nd+1208`, range `f64 nd+704`, center point `nd+1192` / `+1200`,
  scale H × 0.6775. 23475's range + 10 NM flag is **ignored** by 23452.
- It sits between the clip push (23422, lines 2255 / 2377) and the pop (23420, line 3763): traffic is cut by the map
  clip (expanded the circle of radius H × 0.6775 − 66; centered the rectangle below H × 0.14) and the screen's edges.
- No ABOVE/NORM/BELOW band: the builder's band is fixed (±2700 ft), its range 40 NM whatever the map range.

### Who fills it (46518, 46549)

- **Update** 46518 (each tick): `u8 tcas+888` = `u8 [f31452(air_ground, 11)]` (on the ground; live 1);
  `i32 tcas+796` the sensitivity level = f46519(radio altitude f46419(ra), own altitude `f64 [adc]+40`): RA < 1000 →
  1, < 2350 → 2, then altitude < 5000 → 3, < 10000 → 4, < 20000 → 5, < 42000 → 6, else 7. Then by the transponder mode
  `i32 [tcas+764]+432` (getter 48855): **3 → level forced 1 (TA ONLY); 4 → the altitude level kept (TA/RA); anything
  else → 0**. Then `i32 tcas+800` = 0 (level 0), 1 (level 1), 2 (level ≥ 2).
- **Builder** 46549(tcas, aircraft list), through 46625 ← 46624 ← 46623 ← 46622 (a std::function callback registered
  in 46510 through f71167 on the SimConnect manager at 1171248): exit while testing (+891; the list is kept); clear
  +736 (46550); save and clear +889 / +890; **exit with the list empty when TCAS failed or unpowered or +796 = 0**.
  For each aircraft record (stride 56):
  - skip when own RA < 1750, not on the ground (+888 = 0) and the intruder's altitude − the ground elevation (own
    altitude − RA) < 360 ft;
  - skip when the intruder's ground speed < 30 kt;
  - skip when the horizontal distance (haversine, NM, 23445) > 40 or |relative altitude| > 2700 ft;
  - classify with the row `tcas+344 + 56 × level` (7 f64: TA tau, RA tau, TA DMOD, RA DMOD, TA ZTHR, RA ZTHR, ALIM):
    RA (3, sets +890) only at level ≥ 2: (tau ≤ RA tau, vertical tau ≤ RA tau, closing) or (slant range ≤ RA DMOD and
    |dz| ≤ RA ZTHR); TA (2, sets +889): the same with the TA values; proximate (1): slant range < 6 NM and |dz| <
    1200 ft; else other (0).
  - Live table (level: TA tau, RA tau, TA DMOD, RA DMOD, TA ZTHR, RA ZTHR, ALIM): 0 [20, −1, 0.3, −1, 850, −1, −1];
    1 [25, 15, 0.33, 0.2, 850, 600, 300]; 2 [30, 20, 0.48, 0.35, 850, 600, 300]; 3 [40, 25, 0.75, 0.55, 850, 600,
    350]; 4 [45, 30, 1.0, 0.8, 850, 600, 400]; 5 [48, 35, 1.3, 1.1, 850, 700, 600]; 6 [48, 35, 1.3, 1.1, 1200, 800,
    700]; 7 all 0.
  - Afterwards the TA aural (46538, 23) when a new TA appears and not on the ground; RA handling otherwise.
- **The other aircraft:** `SimConnect_RequestDataOnSimObjectType` (import 59, only caller 71413), radius **200000 m**,
  type 2 (aircraft). The 56-byte record (71012 → 71015 → SimConnect_AddToDataDefinition): +0 PLANE LATITUDE (deg),
  +8 PLANE LONGITUDE (deg), +16 PRESSURE ALTITUDE (ft), +24 GPS GROUND TRUE TRACK (deg), +32 AIRSPEED TRUE (kt),
  +40 VERTICAL SPEED (ft/min), +48 GROUND VELOCITY (kt).

### The self-test (46524 → 46525, 46526; in 23452 instead of the list while `u8 tcas+891`)

- Starts on `L:B757_PED_TCAS_MODE_KNOB_TEST` (static 1106512, f30544 press) only while +888 (on the ground) and not
  already testing: +891 = 1, +890 = 1, the timer at +832; after 3 s the transponder test is released; after 10 s
  +891 / +890 are cleared and the test-pass or test-fail sound plays (1044232 / 1044304). Power loss clears +891.
- Four fixed entries around the screen center (W ÷ 2, H ÷ 2) = (352, 429.5), drawn by 23552: kind 0, +1000 (`+10`),
  no trend, (W ÷ 2 − 125, H ÷ 2 + 90); kind 2, −200 (`-02`), up, (W ÷ 2 − 125, H ÷ 2 + 275); kind 3, +200 (`+02`), no
  trend, (W ÷ 2 + 125, H ÷ 2 + 250); kind 1, −1000 (`-10`), down, (W ÷ 2 + 125, H ÷ 2 + 100). From the code.

### The knob

- `L:B757_PED_TCAS_MODE_KNOB` (static 1106440, registered in 28910) is copied into `xpndr+432` (48873 → 48854, only
  while that transponder is powered and its byte +429 clear). **3 = TA ONLY, 4 = TA/RA**; 0 to 2 presumably STBY,
  ALT RPTG OFF, XPNDR (the code distinguishes only 3, 4 and other). Live: 0, 1, 2 → +800 0, `TCAS OFF`; 3 and 4 →
  +800 1 and `TA ONLY` on the ground (4 is forced to level 1 below 1000 ft radio altitude, as traced).

## Step 3: The map options

### The three lists

- **Not queried per frame by range.** Proximity lists kept by the navigation database object navdb = `[nd+1216]` +
  584 (`[nd+1216]` the `bbs::b757_super`, getter 23428): every item **within 6 degrees** (planar: Δlon² + Δlat² ≤ 36,
  degrees, no cos(lat)) of the aircraft, **sorted nearest first by that planar distance** (sort 70064, comparator
  70065 against navdb+360), rebuilt by 69566 (the navdb update, called by the b757_super update 4049) only when the
  aircraft has moved 1 arc-minute: f23588(navdb+360, navdb+392) (squared planar) ≥ 0.000277778. 69566 copies the
  position source `[navdb+356]` (super+1216: longitude, latitude) into navdb+360 each call; navdb+392 keeps the last
  rebuild position. Live: navdb+360 (−122.30393, 47.44357) = the aircraft; sortedness checked (max planar 5.9996°).
- **NAV AID, VHF navaids:** navdb+80, `std::vector<entry*>` (getter 69598; count 23435 = (end − begin) >> 2, element
  23436); filled by 69567 (quadtree query 69572 / 69638 on navdb+8, all of `tbl_d_vhfnavaids`, loaded once at startup
  by 69550 through 69548). Live 97 entries. **Entry (72 bytes, not polymorphic), checked live:**
  - +0 `char32_t[4]` ident, NUL-padded ("SEA\0", "ISNQ");
  - +16 `u16` frequency × 100 (11680 SEA, 11030 ISNQ); +18 `i16` DME elevation, feet (369);
  - +24 `f64` range NM from the database (25, 40, 125, 130);
  - +32 `u8` kind, from `navaid_class` (69550): first letter `V`, second ` ` → **0 VOR**, `D` → **1 VOR/DME**, `T` →
    **2 VORTAC**; first ` `, second `D` → **6 DME**, `T` → **3 TACAN**, `M` → 4 (mil TACAN), `I` → **5 ILS/LOC with
    DME**, `N` → 7, `P` → 8 (MLS DME);
  - +33 `u8` class from the third letter: `T` 0 terminal, `L` 1 low, `H` 2 high, `U` 3 undefined, `C` 4;
  - +40 `f64` longitude, +48 `f64` latitude (the DME's when the navaid's is 0, 0); +60 `std::u32string` the ident
    again.
  - First live: ISNQ 5 T 47.43433 −122.30630 0.6 NM; ISEA 5 T; SEA 2 H 47.43537 −122.30962 0.5; ITUC, ISZI, ICJL,
    IBEJ, ICHJ (4.3), IBFI 5; TCM 3 T 47.14767 −122.47504 19.1; IGRF 5 (25.1); PAE 1 L 47.91983 −122.27780 28.6; OLM 2
    H 46.97164 −122.90183 37.4; CVV 1 L 48.24469 −122.72442 51.0; NUW 3 H 48.35493 −122.66179 56.5.
- **ARPT, airports:** fms+9072 (getter 23434), `std::vector` of **48-byte records** (count 23437 = size ÷ 48,
  element 23438); live 41. Record: +0 narrow `std::string` ICAO ident; +16 `f64` longitude; +24 `f64` latitude; +32
  `f64` and +40 `u16` (+40 = 432 for KSEA, probably the elevation; not used by the draw).
  - Filled by 39571 (from 39529, 40288, 40289; assigned through 39757 → f41215): **the FMS's nearest suitable
    airports** (the RTE/ALTN machinery). Rebuilt when the position has moved ≥ 20 NM (haversine 4581) from
    `fms+32992` (latitude, longitude; flag `u8 fms+33008`) or when forced. Query 39746 / 41163: within 300 NM, then
    600, 1200, 2400 until one qualifies (static doubles at 103952); filter 39752: longest runway ≥ `i32 fms+32952`
    feet (live 5000), a surface flag when `u8 fms+32956`, not in the alternates page's own list. Sorted (44070): with
    `u8 fms+1513` (live 1) by great-circle distance from the aircraft, else by time (distance ÷ ground speed × 60).
  - First live: KSEA 0.5 NM, KBFI 5.2, KPWT 18.9, KTCM 19.6, KGRF 24.6, KPAE 27.8, KNUW 56.3, KCLM 63.0, KBLI 81.5,
    CYYJ 85.1, KEAT 85.2, KYKM 89.1, CYXX 94.9, CZBB 101.8, CYVR 110.8 (last KMYL 299.7).
- **WPT, range ≤ 40, enroute waypoints:** navdb+344 (getter 69601; 23440 / 23441), from navdb+272 = all of
  `tbl_ea_enroute_waypoints` (69553), filled by 69570 with the same query and sort (checked live, max 5.9995°); live
  1525. Entry (48 bytes): +0 `f64` longitude, +8 `f64` latitude, +16 `char32_t` ident, NUL-terminated (up to 5 seen).
- **WPT, range > 40, enroute NDBs:** navdb+256 (getter 69600; 23443 / 23444), from navdb+184 = all of
  `tbl_db_enroute_ndbnavaids` (69552), filled by 69569; live 13. Entry: +0 `char32_t` ident, NUL-terminated; +24
  `f64` longitude, +32 `f64` latitude; +40 `f64` (17.0 for FHR; not used by the draw).
- Terminal NDBs (`tbl_pn_terminal_ndbnavaids` → navdb+96 → navdb+168, 69568) and terminal waypoints are never drawn;
  NAV AID draws no NDB.

### What each draw routine draws

- **f23433(nd, entry, tuned), a navaid:** nothing for a null entry; with tuned = 0 nothing if the entry is
  `[nd+212]` or `[nd+216]` (drawn separately). **At a drawn range > 40 only class 2 (H) is drawn, unless tuned.**
  Symbol by kind (f23495(nd, a, b, point, tuned)): 0 VOR a hexagon of radius 14 (6 points at 0°, 60°, …); 1 VOR/DME
  and 2 VORTAC the three-lobed outline (13 points, static 79764: (−15,−19) (−7,−14) (7,−14) (15,−19) (21,−9) (11,−3)
  (6,6) (6,18) (−6,18) (−6,6) (−11,−3) (−21,−9) (−15,−19) px from the point) plus a hexagon of radius 11; 3 TACAN and 6
  DME the three-lobed outline only; 4, 5, 7, 8 and anything else **not drawn, not counted**. Test: 23475 from the
  map center, valid when great-circle distance − range ≤ 10 NM; invalid → nothing, not counted. Label: the 4 UTF-32
  characters at +0 at (x + 20, y + 20), font `nd+336`. Untuned: cyan stroke (1171096), cyan label (1171040); tuned:
  green stroke (1171128), green label (1171072). `nd+1276 += 1` after drawing.
- **f23439(nd, rec, rec+16), an airport:** the first 4 characters of the narrow string to UTF-32, then **f23430**, the
  routine of the origin and destination: the range + 10 NM test; a **cyan circle, radius 15** (f23401, arc 0 to 360,
  stroke 1171096); the name (4 characters) at (x + 20, y + 20), font `nd+336`, cyan (1171040); `nd+1276 += 1`.
- **f23442(nd, position, name), a waypoint or NDB:** nothing if the name pointer is null, or if **f23496** finds the
  position on a drawn route (it walks the route's P, 37385 of `[nd+1244]`, and while a modification is pending the
  modification's P, comparing each point: f24028 count, f24032 element, f5181, f23660), so route waypoints are not
  drawn twice. The range + 10 NM test. A **cyan triangle** outline (f74279, stroke 1171096): apex (x, y − 16), base
  corners (x ± 15, y + 14). The UTF-32 name at (x + 20, y + 20), font `nd+336`, cyan (1171040). `nd+1276 += 1`.

### The loop, the count and `EXCESS DATA` (23406, WAT lines 2616 to 3630; the map block: MAP with `nd+805`, or PLAN)

- **Order:** f23430 origin and destination, f23431 the course lines, `f23433(nd, [nd+212], 1)`,
  `f23433(nd, [nd+216], 1)` (all before the count is zeroed, so never counted), then `nd+1276` = 0 and the loop.
- **Each round:**
  1. NAV AID (`u8 nd+208`): the next pointer of navdb+80 → f23433(nd, entry, 0).
  2. ARPT (`u8 nd+210`): the next record of fms+9072 → f23439(nd, rec, rec+16), unless its name equals the origin's
     or the destination's (`nd+1096`, `nd+1128` converted to narrow strings; f5277 is `!=`).
  3. WPT (`u8 nd+211`): range ≤ 40 the next pointer of navdb+344 → f23442(nd, entry, entry+16); range > 40 the next
     pointer of navdb+256, skipped (the round restarts) when f23445(nd+1160, entry+24) > 80 NM (haversine **from the
     aircraft**, not the map center), else f23442(nd, entry+24, entry). A null pointer also restarts the round (WAT
     `br 7` / `br 6` to the loop head, which still counts a round).
  4. End of round: stop when `nd+1276` ≥ 100, or when every switched-on list is used up (a switched-off list counts
     as done); at most 500 rounds.
- So 100 to 102 items, taken nearest first and interleaved one from each list per round; items failing the range
  + 10 NM test are skipped uncounted; only the first 500 of each list are looked at. The clip and the screen edges cut
  the picture but **not the count**.
- **`EXCESS` / `DATA`** (UTF-32 487440 / 487468): drawn when `nd+1276` ≥ 100 after the loop; translate (50,
  H × 0.57), `EXCESS` at (0, 0), `DATA` at (0, 26), font `nd+336`, **amber** (1171056).
- **Live, range 20, NAV AID, APRT and WPT on** (`u8 nd+208`, `+210`, `+211` = 1): `u32 nd+1276` 100 on three reads a
  second apart (never lower); the capture showed amber `EXCESS` over `DATA` at the left, cyan triangles with names
  (HAVHO, WASRA, VPLSH, VC915, CONIT, VC910, VC900, VC901, HETHR, VC903, JULEN, NEZUG, …), cyan rings KBFI and KSEA,
  at the screen points predicted from the lists (the projection from the nd's center and rotation, the clip circle).
  Predicted uncapped: 2 navaids + 6 airports + 112 waypoints = 120.
- **Live, range 80:** count 17 = the predicted 4 navaids (class H; NUW, OLM, …; the tuned one not counted) + 12
  airports + 1 NDB (FHR). The capture: cyan NUW (three-lobed, TACAN) with KNUW's ring beside it, rings KPAE, KBFI,
  KSEA, KPWT, KTCM, and at the left edge the end of FHR's label (`HR`: the label is drawn although the triangle is cut
  by the edge).

### The tuned stations (`f23433(nd, [nd+212], 1)`, `f23433(nd, [nd+216], 1)`)

- `nd+208` is a small struct the efis panel and the VOR receivers write: +0 to +3 the switch bytes, **+4 VOR L's
  station, +8 VOR R's station** (pointers into navdb+80's records).
- Written by **47476** of each `bbs::vor_control_panel` (fms+7884 = VOR L 0x2c7c90, fms+7888 = VOR R 0x2c7fb0; also
  nd+852 / +860 and nd+864) to both EHSIs (`[vor+356]`, `[vor+360]`): +4 when f8981(vor) == 63, else +8; null in ILS
  mode. The station comes from manual tuning (47474: the first navaid of navdb+80, nearest first, whose frequency
  equals `i32 vor+780` × 100 + `i32 vor+776` and which is received, f47465) or from FMS autotune (47487 from 39700,
  47488 from 39543).
- Drawn with tuned = 1: every kind 0 to 3 and 6 at any range, **green** symbol and ident; not counted.
- Live: `[nd+212]` = SEA (VORTAC, 116.80), `[nd+216]` null; green SEA symbol and label at the aircraft in every MAP
  capture, switches on or off.

### The course lines (f23431, f23494)

- Over the two VOR receivers (fms+7884, fms+7888; f23490 returns 2). For each: skip if null or while `u8 vor+740`
  (23492, the autotune byte) is set: the line is drawn only for a manually tuned receiver; station f47466 (`[vor+368]`, null without panel power f36730(vor+344, vor+736)
  or in ILS mode); course `f64 vor+784` (23466). Skip unless the station's kind is VOR, VOR/DME, VORTAC or 5 (f23470),
  the course is finite, the station is received (f47465) and **its frequency equals the receiver's manual one**
  (`u16 station+16` == `vor+780` × 100 + `vor+776`). Course rounded `% 360`. The second receiver is skipped when it has
  the same station and the same course modulo 180.
- **f23494(nd, station, course):** a line through the station along course + the magnetic variation (f69687 at the
  station), both ways (course, course + 180), projected, clipped to the screen rectangle and, with `nd+805` and not
  centered, to the circle of radius H × 0.6775 − 66 around (W ÷ 2, H × 0.8175). **Green dashes** (stroke 1171128),
  30 px on, 16 px off. Label (only when the visible chord is ≥ 80 px): the course `setw(3)` fill `0` ("130"; the
  reciprocal half (course + 180) % 360, "310"), 100 px from the station along the line (clamped 40 px inside the
  chord), rotated with the line (kept within ±90° so it reads upright), offset (−30, −30), font `nd+336`, **green**
  (1171072). Not counted.
- Live: back in MAP with VOR L tuned manually to SEA and course 187: a green dashed line through SEA labeled `187`
  (below the station) and `007` (above). With autotune back on and the manual frequency 117.40 again the line was
  gone.

### No weather radar

- **The EHSI draws no weather radar.** Live: `L:B757_ADI_1_WXR_BUTTON` = 1 changed nothing on the EHSI (captures
  compared).
- Code: the block at 23406 lines 1345 to 1906 (`u8 nd+778`) is the **terrain** picture (Step 4).
  `L:B757_ADI_1_WXR_BUTTON` (static 1095208, set up in 28598) and `L:B757_ADI_2_...` (1095712) are read only by
  53374, which returns "TERR or WXR on, either side" to the 75-case condition switch 53363 (callers 53360; not a
  display). The pedestal radar panel's L:vars (`B757_PED_WX_L/R_LARGE_SEL_KNOB`, `SMALL_SEL_KNOB`, `TFR`, `WXT`,
  `WX`, `MAP`, `GCS`, `TEST`; statics 1098736 to 1099384, set up in 28696 to 28724) are referenced only by their own
  constructors and destructors. No class with wxr, weather or radar in its name except the CDU's ACARS weather pages;
  no EHSI function holds a WX, TILT, GAIN or CAL string.

### The EFIS switch bytes (36216 setup, 36232 update)

- `efis+568` `L:B757_ADI_1_NAV_AID_BUTTON` → `u8 nd+208`; `efis+640` `L:B757_ADI_1_DATA_BUTTON` → +209; `efis+712`
  `L:B757_ADI_1_APRT_BUTTON` → +210; `efis+784` `L:B757_ADI_1_WPT_BUTTON` → +211 (the FO's statics 1095856, 1096000,
  1095928, 1096072). Written through f23432(`[efis+1012]`).

## Step 4: The rest of the map

### TERR (23427, every mode, called unconditionally from 23406 line 1936)

- **Condition:** `u8 nd+805` AND the L:var at handle **nd+376** non-zero. nd+376 is a 66-byte copy (23370) of the
  static handle 1095136 (Captain) or 1095640 (FO), named `B757_ADI_1_TERR_BUTTON` / `B757_ADI_2_TERR_BUTTON` (at +48;
  checked live), read with f4133(handle, false, 0.1), a plain read. In MAP, PLAN, VOR and ILS, centered or not.
- **The word:** `TERR` (UTF-32 487900), cyan (1171040), font `nd+336`, at (0.71 W, 0.12 H), top right.
- **The two numbers:** f23486(out, f71018(), side) reads the singleton at **1171248** (returned by 71018): record at
  `1171248 + 272 + side × 6`, side 0 Captain (`nd+88` = 6), 1 FO: `s16 +0` the **highest** terrain elevation,
  `s16 +2` the **lowest**, `u16 +4` a flag or spare (live 0); feet.
  - Written only by the SimConnect dispatch 71013 (`SIMCONNECT_RECV` id 16, CLIENT_DATA): payload at recv+40 with
    `u16` magic 30066, +4 = 0, +8 type **4**, +20 the side, +24 the 6 bytes copied (type 5 copies 20 bytes to
    1171248+800, not traced). Computed outside this module and pushed in by client data.
  - **Format:** each ÷ 100 (integer division of the s16), max(0, x) (12255), `setfill('0')`, `setw(3)`; font
    `nd+344`; the highest at (0.845 W, 0.115 H), the lowest 32 px below.
  - **Color per number** (23487, d = the number in feet, undivided, − the altitude): d > 2000 → **red**; d > 1000 →
    **amber**; d > −500 (−250 with the gear down) → **amber** (the same static 1171056); otherwise **green**. So red
    above +2000, amber from −500 / −250 to +2000, green below.
  - **The altitude:** `f64 adc+416` = PLANE ALTITUDE (true, feet), adc = `[instr_source+716]` (getter 44757) through
    f30362(adc) = adc+368, +48. When the vertical speed `f64 adc+424` (ft/min) is below −1000 the altitude is predicted
    30 s ahead: max(−100, alt + VS ÷ 60 × 30). Gear down = the `GEAR POSITION` handle at nd+928 == 1. A byte fr+103
    (highest − alt > 250 / 500) is computed and unused.
- **The terrain picture:** the block at 23406 lines 1345 to 1906, guarded by `u8 nd+778` = TERR non-zero AND not
  centered AND mode ≠ PLAN (23417). A change of +778 (against `u8 nd+779`) frees and zeroes the images +792, +796, +800
  and resets the sweep (+788 = 1, +789 = 1, +780 = 0, +806 = 0). Fields: `f32 nd+780` the sweep angle (±180, frame
  time × 135° a second); `f32 nd+784` the angle kept at a swap; `u8 nd+788` the direction (1 = increasing, from the
  left); `u8 nd+789` the first sweep after a reset; `i32 nd+792`, `+796` two 1300 × 1300 images drawn alternately,
  `+800` the one being rendered; `u8 nd+806` a render requested (at 80 % of the sweep); `u8 nd+1274` the renderer's
  done flag; nd+136 and nd+160 the heading and position each image was rendered for (24 bytes each), nd+184 the
  pending one. Rendered by the global object at 1171248 (f71018), requested through f23373 (`1171248 + 264 + side × 4`
  the image, +256 a vector, +784 the flag pointer nd+1274); the image data arrives by the same client-data dispatch
  (type 824 → f77972 texture upload). **A texture: it cannot be listed**; only its presence can.
- **Live:** `L:B757_ADI_1_TERR_BUTTON` = 1: `u8 nd+778` 1, `f32 nd+780` moving; side 0 3123, 485; the adc altitude
  396.9, VS 0. Screen: cyan `TERR` top right, to its right `031` in red over `004` in amber (3123 − 397 > 2000 → red;
  485 − 397 = 88 → amber), and the picture (cyan water, amber terrain patches). The highest number had read 2808 half
  an hour earlier, parked the whole time: the values change with the sweep or refresh.

### The wind (23426; MAP, VOR, ILS; not PLAN; only while `u8 nd+805`)

- **The show flag**, static byte **1008842**, written only in 23426: wind speed (int) > 6 → 1; < 4 → 0 (4 to 6 keeps
  the last value); then ground speed `f64 nd+736` (GROUND VELOCITY, kt) < 100 → 0.
- **Speed:** `AMBIENT WIND VELOCITY` (kt; handle nd+608, registered in 23366) through f23485, truncated to int (f4283,
  trunc); `setfill(' ')`, `setw(3)` (" 70"). Flag 0: `---` (UTF-32 487920) and no arrow. White (1171064), font
  `nd+312`; translate (0.048 W, 0.895 H), the text at (0, 0), bottom left.
- **Arrow** (flag set only): translate (27, −38) from the text origin, rotated by **`AMBIENT WIND DIRECTION`
  (degrees, handle nd+640) − `f64 nd+1208`** (the map rotation), clockwise (f22940 → 78091 → 78126, × π ÷ 180). A line
  (0, −30) to (0, 30) and a head (−5.25, 24.75), (0, 30), (5.25, 24.75), white pen 1171120; unrotated the head points
  down, so the arrow shows where the wind blows to and its tail where it comes from, relative to the top of the map.
  Both angles are true in MAP: "wind 70 kt from (direction − rotation) normalized to ±180".
- **No direction number is printed.** Live parked: wind 2.0 kt from 40.0°, flag 0, `---` (plan 26's cruise: flag 1,
  70 kt from 287°).

### The runways (23446; `nd+968` departure, `nd+1032` arrival; the map block: MAP with +805, or PLAN)

- **Record (64 bytes):** +0 end 1, the selected threshold (f64 longitude, f64 latitude); +16 end 2, the far end; +32
  the name, a **narrow** libc++ `std::string`; +48 the length in meters (f64); +56 the course (f64, degrees true).
  Drawn only if the name is non-empty (f4149).
- **Filled each frame by 23411** (from 23406 line 833) from the route (nd+1244): departure when `route+1336` (the
  departure runway name) is non-empty: the navdb runway copy `route+1864` (37761) and the point `route+1728` (37763);
  arrival when `route+1372` is non-empty: `route+1928` (37762), `route+1760` (37764). 23462 builds it: name = the
  navdb name.substr(2, 3) ("RW16L" → "16L"); length = navdb `f64 +8` (feet) × 0.3048; end 1 = the navdb threshold
  +32 (lat, lon swapped to lon, lat); +56 = the initial great-circle bearing (23663) from the threshold to the route's
  point (+1728 / +1760); end 2 = from end 1 along the course by the length (4275). Empty name: the record's name is
  cleared and nothing drawn.
- **Drawn** (no range test of its own; both ends through 23475 and the clip): two side lines, the end-1 to end-2
  segment offset ±10 px (f23499; a 20 px outline, white pen 1171120, length to scale); dashed extended centerlines
  **6.5 NM** beyond each end (f4055), dashes of 10 px every 20 px (white); the name in white (1171064), font `nd+336`,
  60 px from the outline's first corner along an angle computed from PLANE HEADING DEGREES TRUE (handle nd+448, unit
  "Radians") minus (course − 90), passed to a degrees routine (looks like a unit mix-up in BlueBird's code; only the
  label's place depends on it). 23446 uses +56 − 90 for the centerlines (screen angles from +x in f23502).
- **Live** with RW16L and ILS 10L: `nd+968` `16L`, end 1 47.46379 −122.30775, end 2 47.43117 −122.30804, 3627 m,
  course 180.34 (true); `nd+1032` `10L`, end 1 45.59480 −122.59561 (= the RW10L leg), end 2 45.58169 −122.56199,
  2995 m, 119.11. Captures at range 10 and 20: the 16L outline through the aircraft's position, white dashed
  centerlines both ways and the label `16L`. The names are the stored narrow string (no `RW`).

### The green rings: the deceleration's start and end (P+4276, P+4288)

- **Drawn** in 23448 after T/D by 23533: for each element a green ring (pen 1171128, radius 7.5) where it projects
  (23475 valid); no label. Element 16 bytes, (f64 longitude, f64 latitude) (23510 → f23615, pushed through f4057).
- **Filled only by 24135** (from 24105, the VNAV path recompute; 24114 and 5232 only clear them): both vectors
  cleared, then the descent profile `P+3744` (a vector of 264-byte segments, 24176) walked from index 1 for a run of
  consecutive segments with `u8 seg+136` set; `seg+32` is the segment's distance before the end-of-descent reference.
  s112 = +32 of the segment before the run (its near end), s104 = +32 of the run's last segment (its far end), s80 =
  s104 + 0.4 NM if the next segment is at least 0.4 NM further. The reference s128 = `P+128[k]` (the cumulative
  along-path distance of leg k, an f64 vector), k = f24167: the last leg (searching back from the end, before the
  missed-approach index `P+4256`) whose waypoint description (`leg+640`, first character) is **'G'** (runway) or
  **'E'** with a constraint kind 1, 2, 4 or 5. The run is used only if s128 − s112 > `P+3456` (the aircraft's
  along-path distance, written by 24216). Then **P+4276 = the point at s128 − s80** (where the run begins) and
  **P+4288 = the point at s128 − s112** (where it ends), each through 24170 (linear interpolation along P+2864 by the
  cumulative distances at `P+2888[i]+40`). Cleared before each push: **at most one point each**, the last qualifying
  run, the one farthest from the runway still ahead.
- **`seg+136`:** set to 1 by the segment builder 24436 in the branch that stores a target speed in `seg+88` (Mach
  when ≤ 1, from input +128 / +136), a speed type at +96 / +100 and a rate at +80 (default 500); 24438 clears it when
  it merges segments. So the flagged segments are **speed-change (deceleration) segments**; the name is inferred from
  the fields, no string names it.
- **Live:** `P+4276` 1 point, 46.21951 −122.83321; `P+4288` 1 point, 46.11832 −122.82398 (76.6 and 82.4 NM along the
  descent toward KPDX). `P+3744` 75 segments; flagged 12 to 18 (180 to 240 kt at 3,100 to 3,900 ft) and 39 to 44 (250
  to 296.9 kt at 10,200 to 10,700 ft, `seg+32` 37.04 to 41.73 NM before the end of descent). The two rings bracket
  39 to 44, the 250 kt / 10,000 ft deceleration: the start and the end of the next deceleration ahead, as traced.

### The PRED ETA-ALT marker (`u8 P+200`, `i32 P+204`, `f64 P+208`)

- **The FIX INFO page's PRED ETA-ALT** (CDU strings `CDU_FIX_PRED_ETA_ALT`, `U:(s:s;)-----`, `(s:s;)Z`). Set by
  40277 (from the CDU dispatcher 40155): an altitude → 24574: +200 = 1, **+204 = 1**, +208 the altitude (ft); a time
  (HHMM) → 24575: +200 = 1, **+204 = 2**, +208 the time as a number; `DELETE` → 24549 clears (+200 0, +204 0, +208
  −1). Only the active route's P (40277 resolves the CDU side's route through f39168, then P by 37385).
- **Auto clear** (24548, every draw): altitude type when |`P+3528` (the aircraft's altitude used by VNAV, f5236) −
  value| ≤ 100 ft; time type when the clock (`P+184` if `u8 P+176`, else the ZULU static 74319) is more than 30 s past
  the value (24551 → seconds, wrapped ±12 h).
- **Position, not stored:** altitude type 24550 searches the climb profile `P+3720` first, then the descent profile
  `P+3744` (segment altitude at +0), for the crossing of the value; time type 24552 the point whose predicted ETA
  equals the time. Both give an along-path distance turned into a point by 24170.
- **Drawn** in 23448: a green ring (pen 1171128, r 7.5) and green text (1171072), font `nd+336`, at (+15, +15) px.
  Text (24586): altitude → round(value), a plain integer, no padding, no unit ("11000"); time → `setfill('0')`,
  `setw(4)` of trunc(value), then `z` ("1532z"); otherwise empty.
- **Live:** PRED ETA-ALT 10000: `u8 P+200` 1, `i32 P+204` 1, `f64 P+208` 10000.0; the CDU line read `6NM 10000`; the
  capture showed a green ring on the route about 6 NM along it with green `10000` beside it. P+176 to P+268 hold only
  those three: no field near P+200 holds the position or the distance.

### S/C (39681)

- **Gate in 23448:** f39690(fms), valid when `i32 fms+34980` (the STEP SIZE: −1 = ICAO, the step then taken as 2000;
  0 = no step climbs → nothing; other = feet) ≠ 0; live −1. Then 39681(out, fms, route) on the route drawn; drawn when
  the result is valid, its "behind" flag +72 is 0 and the position is non-zero: a green ring r 7.5 (pen 1171128) and
  `S/C` green (1171072), font `nd+336`, at (+15, +15) px.
- **Inputs:** P (route → 37385); `P+3456` the aircraft's along-path distance; the cruise altitude `f64 route+640`;
  the pressure altitude `f64 adc+408` (adc = `fms+7924`, f30362 +40; live 82.97 = PRESSURE ALTITUDE); `u8 fms+1513`
  (when set the current-altitude test is skipped; live 1 parked; the trend vector is hidden while set: probably "on
  the ground", not proven); per leg `i32 leg+1496` the planned step altitude, `f64 leg+296` the along-path distance,
  `f64 leg+1480` the ETA, the leg position (44630); the step list `P+3792`, 24-byte elements {f64 altitude, f64
  along-path distance, f64 ETA}.
- **The computation, two passes:**
  1. Legs 0 to n − 1 (f24028 counts flown + remaining): skip unless the step altitude > 0 and `leg+296` finite and
     ≥ 0; skip if |cruise − step| ≤ 100, or (fms+1513 = 0 and |pressure altitude − step| ≤ 200). Otherwise a
     candidate {type 1, leg index, leg name (44633), altitude, d = leg+296, ahead = max(0, d − P+3456), ETA, the leg's
     position, behind = d < P+3456 − 0.05}. Kept: the farthest-along behind candidate and the first ahead one. Result:
     the behind one if any (then not drawn, +72 set), else the first ahead.
  2. If pass 1 found none: walk P+3792; skip altitude ≤ 0 or distance < 0, or (fms+1513 = 0 and pressure altitude ≥
     altitude − 200); the first left gives {type 2, altitude, d, ahead, ETA (+16), position = 24170(P, d)}.
- **Output (76 bytes, sret):** +0 valid, +4 type (1 leg, 2 list), +8 leg index, +12 name (`std::string`), +24
  altitude, +32 along-path distance, +40 distance ahead, +48 ETA, +56 position (f64 longitude, f64 latitude), +72
  behind. **Stored nowhere**: the draw consumes and frees it (f23531).
- **Reproducing it:** 39681 is 290 decoded lines (about 1970 WAT lines), plus 24170 (about 70) and 39690. Every input
  is a plain field the reader already reaches (route, legs, P, P+2864, P+2888) plus four new ones (fms+34980,
  fms+1513, fms+7924 → adc+408, P+3792); the arithmetic is a filter and one linear interpolation, no profile
  computation. Judged modest effort and low risk; the meaning of fms+1513 and the "behind wins" rule are odd, so the
  code would be copied literally and checked on one flight with a step on the route.

### The leg position rule for kind 2 (44630)

- Live: for a leg of kind (`+72`) 2, 44630 decides by the leg's **capability set at `leg+784`**, a libc++
  `std::unordered_set<int>` (bucket array +784, bucket count +788, first node +792, size +796; node: next +0, hash +4,
  value +8). In order: contains 0 → primary (+832, +840); contains 1 → primary; contains 5 → second (+880, +888);
  contains 19 or 13 → second if it is not (0, 0), else primary; contains 2 → second; contains 3 → primary; else
  primary.
- Live: FIX INFO's TCM read {9, 19, 18, 3} → second: primary (0, 0), second 47.14767, −122.47504 (the right one).
  Plan 26's reader takes the primary only, so a TACAN-only leg on a route would read (0, 0).

### Drawn but outside plan 28's list

- **23449 (MAP and PLAN): the FIX page's rings and radials.** FIX 1 (`u8 fms+7224`, `fms+5560` a fix leg object) and
  FIX 2 (`u8 fms+7225`, `fms+5564`): 23534 draws a green circle of radius 27 px around the fix (pen 1171128) and, for
  a navaid (`fix+72` = 2), its navaid symbol (VOR/DME/TACAN from `fix+772`'s letters 'V' and 'D' / 'T' / 'M'). Then
  three entries of the vector at `fms+7280` (FIX 1) / `fms+7292` (FIX 2), 8 bytes each: `i32` radial, `i32` distance
  (−1 = none). A distance → a dashed green circle of that many NM around the fix, the integer distance as a green label
  (font `nd+336`) just below its bottom (+10 px). A radial → a dashed green line from 27 px out, 18 dashes of 30 px
  every 46 px (to about 810 px), at (radial − 90 − `nd+720`) in MAP, VOR and ILS, in PLAN at (radial − 90 + the
  magnetic variation at the fix, f69687).
  - Live: FIX INFO TCM: `u8 fms+7224` 1, `[fms+5560]` a 1632-byte leg-shaped object, kind 2, the narrow name at +816
    `TCM`, the class string at +772 ` TT  `, the position by the rule above. `300` on line 2 stored (300, 300) in the
    first entry of `fms+7280` (the CDU showed `300/300`); `/15` on line 3 stored (−1, 15). The capture: a dashed green
    circle of 15 NM around TCM (its top crossing the bottom of the screen at range 40).
- **23450 (MAP and PLAN): the OFFPATH DES circles.** When `u8 fms+1420` (toggled by the CDU action
  `TOGGLE_OFFPATH_DESCENT_MODE`, 40247; the DES page's OFFPATH DES `OFF/ON`) and the data is valid (`u8 fms+1424`,
  `u8 fms+1425`, the point at fms+1448 non-zero): two dashed circles around fms+1448 (longitude, latitude; probably
  the E/D or the destination), radius `f64 fms+1488` NM (white pen) and `f64 fms+1496` NM (cyan pen 1171096): the
  clean and speedbrake off-path descent ranges. No text. Live (earlier, no route): 0.
- **23453 (MAP with +805): the altitude range arc.** When the MCP altitude `f64 mcp+856` differs from the indicated
  altitude `f64 adc+400` by more than 50 ft, the vertical speed `f64 adc+424` is at least 0.1 ft/min in magnitude and
  toward it, and not on the ground (f31454(air_ground)). Distance (NM) = |Δalt ÷ (VS ÷ 60)| s × ground speed
  `irs+3952` ÷ 3600, along the true track `irs+3920` from nd+1160; a green arc (pen 1171128), radius 250 px, 60° wide,
  centered on that point's bearing.
- **23458 (MAP only): the VNAV path deviation scale**, right side at (0.91067 W, 0.5613 H), 0.2567 H tall: white
  ticks and a magenta diamond (pen 1171136) at `P+3936` (the path deviation, ft, clamped ±400, smoothed into
  `f32 nd+752`). Only when the route's P exists and `i32 P+3916` (the VNAV phase, getter 24289) = 4 (descent, in
  24290). No number printed. The same value goes out as `L:BBS_VNAV_PATH_DEVIATION` (registered in 24290).
- **23451 (MAP with +805): the position trend vector.** Hidden while the range changes (`u8 nd+804`), while
  `u8 fms+1513`, or at a ground speed ≤ 1 kt. From the selected IRS (44812 → irs+3904): ground speed `irs+3952`, true
  track `irs+3920`, bank `irs+3944` (negated). Segments of 30 s (1 at range 10, 2 at 20, 3 above) with 5 s gaps;
  straight when |bank| ≤ 0.25°, otherwise curved at the turn rate g·tan(bank) ÷ v, capped at 120° of turn. Pen
  nd+352 (white). Graphic only.

## What the reader takes (the controller's rulings)

- No weather radar rows: the EHSI draws none; the WXR switch changes nothing on this display.
- TCAS traffic: every drawn symbol (MAP only, IRS valid; other and proximate only while TFC or the test) inside the
  screen clip, in the window's screen order.
- Map options: the reader replays BlueBird's loop (one item from each switched-on list per round, the range + 10 NM
  test, the count, the stop at 100 at a round's end, at most 500 rounds, the class-H filter above range 40, the 80 NM
  test for NDBs, route waypoints skipped) and lists the drawn items inside the clip. The tuned stations and the VOR
  course lines are listed in every MAP and PLAN reading.
- Green rings: named from the structure as the start and the end of the next deceleration ("Map: deceleration
  starts, <place>", "Map: deceleration ends, <place>").
- The PRED ETA-ALT marker and S/C have no stored position: the owner decides (compute as BlueBird does, or leave
  them unread).
- Measured but outside this plan's list, put to the owner: the FIX page's rings, circles and radials (23449), the
  OFFPATH DES circles (23450), the altitude range arc (23453), the VNAV path deviation scale (23458), the position
  trend vector (23451).
- The bearing pointers (23457) belong to the VOR/ILS picture and are read.
- The leg position rule for kind 2 is fixed in the reader (the capability set).
- **An L:var the draw reads through a variable handle is read from the handle's cached value, `f64` at handle+16**
  (measured: the nd's TERR handle `nd+376`: +16 read 0.0 with `L:B757_ADI_1_TERR_BUTTON` 0 and 1.0 with it 1, the
  static original 1095136 staying 0; the RMI selector's static handle 1112416: +16 read 0.0 and 1.0 with
  `L:BBS_DDRMI_LEFT` 0 and 1). The simulator-variable handles (the wind's `nd+608` and `nd+640`, the gear's
  `nd+928`: a different class, with the unit's name inside) hold no value: the wind speed, the wind direction and
  the gear position are simulator inputs (S10): `AMBIENT WIND VELOCITY`, `AMBIENT WIND DIRECTION`, `GEAR POSITION`.
- Not read in this plan: the amber `VOR` flag (its condition is the electrical circuit lookup f36730, not a field).

## Settled from the code after the reviews (2026-11-12)

- **f23445** (the NDBs' 80 NM test) is f4581 on the two positions: the top line's haversine (R = 6,371,000 m, ÷ 1852).
- **The second course line** (23431): the second receiver is compared only with a line already drawn in this frame
  (the previous station and course are kept after f23494 draws); "the same station" is the same frequency, the same
  ident (the string at station+60) and a position within 1e-6 (f23493).
- **f47466**, the station getter the course lines and the tuned-station write use, returns no station while the VOR
  panel's circuit is not available (f36730(`[vcp+344]`, `vcp+736`)) or while the panel's EFIS is in ILS mode; the
  field `vcp+368` keeps its station meanwhile. The reader reads the field: the panel's power is not modeled.
- **f23660**, the route-point comparison of the waypoint draw's on-route test, compares both coordinates through
  f23841 (not decoded); the reader takes exact equality.
- **The TCAS message draw (23460), re-read:** `TCAS` / `FAIL` is drawn, and ends the left block, only when the
  failed-or-unpowered test (f73997) AND `u8 nd+1273` both hold; failed with the gate clear falls through to the level
  switch: level 1 draws `TFC` (if TFC and the gate) and `TA ONLY` (ungated), level 2 `TFC` (gated), any other value
  `TCAS` / `OFF` (gated).
- **The map block's condition** (23406): it runs in MAP only while `nd+805`, and in PLAN regardless.
- **The amber test colors:** the ILS dots (`u8 nd+274`) and the glideslope scale (`u8 g+39`) are amber during the
  receiver's self-test.

## Not measured

- **VOR and ILS:** the `VOR`, `LOC` and `G/S` flags and a failed ILS receiver (23459, 44746); the ILS self-test
  (44742); a centered, unpinned localizer or VOR bar; full ILS with a signal (its scaled pointer, 23345 / 23346); the
  bottom-right text with a frequency set but no localizer (110.50, 110.35: `nd+280` read, the screen not captured);
  the TRU shift of the VOR course (23412); an ADF bearing pointer (selector 1, 23457) and a VOR R pointer; the sign of
  `S+8` itself (the reader takes `nd+240`, whose sign was measured); who writes S (b757_super+1032) and `dme+352` in
  ILS mode (probably 69602 in the ILS panel update); the meaning of the station type byte values (VOR-capable 0, 1, 2,
  5; DME-capable 1 to 8; ILS DME 5); the exact semantics of f36730 / f36697 (a bitset lookup at electrical+344+10640
  +352 by id, read as "circuit available").
- **TCAS:** `TCAS FAIL`; the self-test (`     TEST`, red `TRAFFIC`, the four fixed targets; 46524 to 46526); an RA
  (red square, red `TRAFFIC`); TA/RA above 1000 ft radio altitude (+800 = 2, `TFC` alone); `TCAS OFF` gone in PLAN
  (`nd+1273` 0, from 23416); the names of knob values 0 to 2; the 30 kt ground-speed filter against the taxiing
  aircraft listed as a TA (its ground speed was not read); where the 56-byte aircraft records sit between the
  callback and 46549; the sizes of the fonts at `nd+312` / `nd+336`; the units inside the tau computation (f4391,
  46530, 46533).
- **`u8 vor+741`** (written by 47476 with the tuned station): not identified. The autotune byte is `vor+740`
  (re-read in 23431: the loop goes on to draw only when 23492 returns 0; live +740 read 1 with autotune on and 0
  after VOR mode, when the line was drawn).
- **The map options:** the text anchor of f22881 (labels at +20, +20; alignment not traced) and whether a 3-letter
  ident's NUL renders (assumed invisible); what f8981(vor) == 63 names (presumably the left receiver's id); what the
  75-case switch 53363 does with "TERR or WXR on"; the airport record's +32 and +40.
- **TERR:** which module computes the highest and lowest numbers and the raster (client data magic 30066, types 4 and
  824; the area's name not traced); whether the numbers are the highest and lowest in view (they changed 2808 → 3123
  parked); the gear-down threshold and the 30 s descent prediction; the green color.
- **The wind** on screen with a ground speed of 100 kt or more (plan 26's cruise read the flag 1 with 70 kt; no
  capture), the arrow's direction.
- **The runways:** the label's angle (PLANE HEADING DEGREES TRUE in radians against a degrees routine).
- **The green rings:** the name (deceleration) is inferred from the fields; a third ring in plan 26's capture is not
  explained (plan 26's evidence says three rings; at most two come from P+4276 / P+4288; probably the PRED ETA-ALT
  marker or a FIX ring, not checked).
- **The PRED ETA-ALT marker:** the time type (`1532z`) and the auto clear (24548).
- **S/C:** none on this route (no step), so 39681 has not run live; the meaning of `u8 fms+1513`.
- **A kind-2 leg on a route** drawn on the map (no route leg of kind 2 was available).
- **Outside the list:** the FIX radial's line on the screen (the capture reported only the 15 NM circle); the OFFPATH
  DES circles, the altitude range arc, the VNAV path deviation scale and the trend vector need a flight or the DES page.

## For the recipe (plan 28, Task 3)

Approach (VOR, ILS, DME, bearing pointers):

1. Mode dispatch: the tail of 23406, `fr+284` = f19961(efis); it calls 23455, 23341, 23456, 23365, 23457, 23459 and
   23461 by number (23458 in MAP, 23460 always).
2. Constants: 488156 (0.455), 488160 (0.596), 488164 (0.275), 488168 (0.325), 487432 (0.8175), 487436 (0.6775); in
   23341 the literals 0.0725, 0.02, 0.065, 2.5, 142 ÷ 1000, 580, −110, 67.5, 90, 15, 8.5, 21, 62, 44; in 23456 0.908,
   0.72, 4.35; in 74269 2.35; in 23459 0.9, 0.089, 0.046, 0.03731, 0.056, 2.9, 0.75; in 23365 0.835, 0.895, 0.048,
   +31.
3. nd fields: 23412 writes +224, +248, +256, +264, +265, +268 (direct stores; +876 / +880 the magvar cache). The ILS
   fields are written through **f23432 (returns nd+208)** by 44742, 44743 and 44746 at +24 (232), +32 (240), +64 /
   +65 / +66 (272 to 274), +72 (280), +80 / +81 (288 / 289). The receivers are systems 95 / 96 / 97 (f44744(f963(),
   id) → `[986912 + 8 × id]`, class `N3bbs12ils_receiverE`): nd at receiver+356, pfd +352, panel +348, the signal
   `[r+364]+448` (f44737, f69610). 23416 writes nd+1273 and forces nd+756 to 0 in VOR and ILS.
4. VOR panel: 23366 stores `nd+852` = f23381(mgr, 63 or 64) (also +860, +864); fields +368 station (f47466), +372 the
   ILS-mode station (f47467), +740 auto, +768 / +772 the auto frequency, +776 / +780 the manual frequency (f23468
   reads +776 as i64), +784 course (f23466), +736 the circuit id; handles set in 47469 (f23370(vcp+offset, static)):
   +520 course 1038328 / 1038400, +448 1091392, +376 1091464, +592 1038184, +664 1038040; names in 27010 to 27021 and
   28492 to 28495. 47472 copies the course; 47474 tunes the manual frequency; 47476 writes nd+208's +4 / +8.
5. ILS panel: `[r+348]`, frequency i64 at +360 (f44725: fraction × 0.01 at +360, MHz at +364, −1 = none; f23192,
   f23193); the course static 1039120 (`BBS_ILS_COURSE`, 27040), read by f44726; f44731 its update (knob handles
   1039264 / 1039192). The receiver the instrument source selects: getter 44760 → `[instr_source+736]`.
6. PFD block: f23560 → pfd+136 (+16 the glideslope deviation, +33 the scale, +35 the pointer, +39 the test).
7. DME: 23366 `nd+856` = f23382(mgr, 85 / 86); 32466 → +352 station; 32467 → +360 distance; 73997 → +4 / +5.
8. Station record (navdata, e.g. 0x2954f520): ident UTF-32 at +0, `i16 +16` frequency × 100, `u8 +32` type,
   `f64 +40` longitude, `+48` latitude. DME-capable f23469; VOR-capable f23470; reception f47465 (f69679); bearing
   f4582; magvar f69687.
9. Bearing pointers: 23457 through f23561; selector statics 1112416 / 1112488 (`BBS_DDRMI_LEFT` / `RIGHT`, 29076 /
   29078), 1112776 / 1112848 (`_FO`); ADF `nd+868`, f23565 / f23566, position at +24.
10. Words: `VOR L` 487040, `VOR R` 487064, `TO` 487088, `FROM` 487100, `ILS` 487120, `------` 487136 (23365); `VOR`
    487552, `LOC` 487568, `G` 487584, `/` 487592, `S` 487600 (23459); `DME` 487768, `  ---` 487684 (23425).
11. Stream formats: fixed = table index 902 → f9878 (setf 4, 260); precision f9880; setfill f8589; setw f8587.

TCAS:

12. tcas_computer: `[nd+848]`, stored by 23366 (static slot 0xf12c8); 23406 calls 23460 (line 3940) and, in MAP with
    +805, 23452 (line 3741).
13. Getters on the tcas_computer: 46509 → +736 (the vector; the 40-byte stride from 23553); 46534 → +800; 46535 →
    +889; 46536 → +890; 46537 → +891; 73997 → +4 / +5 (power, fail). 46518 writes +796, +800, +888; 46549 writes the
    vector and +889 / +890 (the cap constant 30, thresholds 2700, 40, 6, 1200, ±500, 1750, 360, 30); 46519 the level
    breakpoints; the threshold rows at tcas+344, 56 bytes a level.
14. 23416 writes nd+1273 (a switch on the efis mode). 23460's constants: 0.041, 0.199, 0.433 (the left block), 30
    (the line step), 0.7768 / 0.38 (`TRAFFIC`); strings 487944 `     TEST`, 487984 / 488088 `TCAS`, 488004 `FAIL`,
    488024 / 488072 `TFC`, 488040 `TA ONLY`, 488108 `OFF`, 488124 `TRAFFIC`.
15. 23552: the kind br_table (0 to 3), the size 25 (from 23452's frame +292), the sign strings 488376 `+` / 488384
    `-`, setw 2 and fill '0' (f8587(2), f8589(48)), ÷ 100 with trunc; 23475 for the placement; the shapes 23519,
    23359, 23404, 74278.
16. The transponder mode: `[tcas+764]` (= 48869(xpndr_control_panel at tcas+788), whose +368 selects 1 or 2) → +432
    (48855), from `L:B757_PED_TCAS_MODE_KNOB` (static 1106440, 48873); the test `L:B757_PED_TCAS_MODE_KNOB_TEST`
    (static 1106512).

Map options:

17. navdb: `[nd+1216]` (23366's store) → getter 23428 (+584). Lists: 69598 → +80 (navaids), 69601 → +344
    (waypoints, range ≤ 40), 69600 → +256 (enroute NDBs, range > 40). Their fill: 69566 (the update; position source
    `[navdb+356]`, positions +360 and +392, threshold 0.000277778), 69567 / 69570 / 69569 (the radius constant 6 in
    each), the sort comparator 70065 (+360).
18. Navaid record: 23433 loads `u8 +32` (br_table over 0 to 7), `u8 +33` (compared with 2), `+40` 16 bytes (the
    position), the text at +0, length 4; the kind mapping in 69550 (the `navaid_class` string, letters V, ' ', D, T, M,
    I, N, P; H, L, T, U, C). Frequency `u16 +16` (69550 stores `navaid_frequency` × 100; compared in 23431 and 47474).
    The three-lobed outline's static 79764; f23495 the symbol.
19. Airports: 23434 → fms+9072; stride 48 (23437 / 23438); the text f298 on +0 (narrow string) in 23439; the
    position +16 passed to 23430.
20. Waypoints, range ≤ 40: 23442 called with (entry, entry+16) in 23406; range > 40: (entry+24, entry), and f23445
    with the constant 80 (and 40 for the range switch); f23496 the on-route test; f74279 the triangle.
21. Tuned stations: f23432 → nd+208; loads of +4 and +8 in 23406 / 23433; written by 47476 (f8981 == 63 picks +4).
22. Course lines: 23431 → 23488 (fms+7884), 23489 (fms+7888), 23492 (+740), 47466 (+368), 23466 (+784), 23468 (+776,
    i64), 23494 (the dash constants 30 / 46, the labels setw 3 fill 48).
23. Colors: the f8962 / f22941 statics, initialized in 69511 to 69540 (f888 r, g, b).
24. The count and the message: `nd+1276` zeroed at 23406's WAT line 2785, compared with 100 at lines 3424 and 3489,
    500 at 2849; `EXCESS` 487440, `DATA` 487468, the height factor 0.57, x 50, y offsets 0 and 26.
25. The projection's valid test: 23475, (distance − `f64 nd+704`) > 10 → invalid; output u8 valid, f64 x at +8,
    f64 y at +16.
26. EFIS switch bytes: 36216 setup, 36232 update, `efis+568`, `+640`, `+712`, `+784` → nd+208 to +211 through
    f23432(`[efis+1012]`).

Map extras:

27. TERR: 23427 loads `u8 nd+805` and calls f4133(nd + 376, 0, 0.1) (the handle offset is the constant 376 in 23427
    and 23417; 23366 copies the static 1095136 / 1095640 into it with f23370); the numbers f23486(out, f71018(),
    side) read `[(obj + 272) + side × 6]` (the constants 272 and 6 in 23486); 71018 returns the constant 1171248; the
    writer 71013 (magic 30066, type 4). Colors: 23487, constants 2000, 1000, −250, −500; the altitude
    f44757(instr_source) (→ +716), then f30362 (+368) and loads at +48 and +56. Positions: constants 0.71 / 0.12 and
    0.845 / 0.115 in 23427. The word `TERR` 487900.
28. The terrain picture: 23417 (the nd+376 handle, +778 / +779), 23418 (the sweep), 23419 (the image draw, size
    1300), the renderer 71018 (1171248), f23373 the request.
29. Wind: 23426; the static byte 1008842 (`i32.store8 offset=1008842`), thresholds 6, 4, 100; handles nd+608
    (velocity, f23485) and nd+640 (direction, f4257); the rotation subtracts `f64 nd+1208`; position 0.048 / 0.895;
    `---` 487920.
30. Runways: 23446 called from 23406 with `nd + 968` and `nd + 1032`; record offsets +0, +16, +32 (string), +48, +56
    inside 23446; the filler 23411 → 23462 (getters 18885 → route+1336, 15122 → route+1372, 37761 → +1864, 37762 →
    +1928, 37763 → +1728, 37764 → +1760); the constants 6.5 (NM) and ±10 px in 23446.
31. Green rings: getters 24182 → P+4276, 24183 → P+4288 (called in 23448), drawn by 23533; element 16 bytes (23510 /
    23615); the filler 24135 (P+3744 through 24166 / 24168, stride 264 in 24176; seg+136, seg+32; P+128 through
    24054, stride 8; P+3456; the constant 0.4); f24167 the reference leg; 24170 the interpolation.
32. The PRED ETA-ALT marker: 24548 (`u8 P+200`, `i32 P+204`, `f64 P+208`), the text 24586 (setw 4, fill '0', 488796
    `z`); setters 24574 (type 1), 24575 (type 2), the clear 24549; getter 24576 → P+200; positions 24550 (altitude)
    and 24552 (time).
33. S/C: 39681 (called in 23448 after f39690 → fms+34980); leg +1496, +296, +1480; route+640; fms+7924, fms+1513;
    P+3792 (38888, stride 24 in 38893); 24546 → P+3456; 24170 the interpolation.
34. The kind-2 leg position: 44630 on the capability set at `leg+784` (bucket array +784, bucket count +788, first
    node +792, size +796; node next +0, hash +4, value +8); the primary +832 / +840, the second +880 / +888.
35. Outside the list: 23449 (fms+7224 / 7225, 5560 / 5564, 7280 / 7292 stride 8 through 23535; 23534 the ring);
    23450 (getters 23539 → fms+1420, 23540 → +1424 / +1425, 23541 → +1448, 23542 → +1488, 23543 → +1496); 23451
    (44812 → irs+3904: +16, +40, +48); 23453 (22147 → mcp+856; adc +400 / +424); 23458 (23571 → P+3936, 24289 →
    P+3916).
