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
  `TERR` (23427, `L:B757_ADI_1_TERR_BUTTON`), the fix rings, the trend vector, the altitude range arc and the VOR and
  ILS pictures are plan 28's.
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
