# The lower EICAS from memory: what plan 25 reads and gates on (plan 25, Task 1)

Measured 2026-11-09, 16:34 to 16:55 local, MSFS 2024, the BlueBird 757 "BlueBird 757-200 RR United Airlines" in
cruise at FL350 (483 kt ground speed), engines running, BBS Nest **not** running (the systems and the EICAS keys
worked without it). MSFSBA was closed. Memory was read only (`PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ`,
a ctypes reader locating the module by the published layout's anchor, `N3bbs14eicas_computerE` at 0x181e4, one
allocation). The only simulator writes were three presses of the lower EICAS's ENGINE and STATUS keys (the owner's
go-ahead), listed in Step 1; the page was left as found. Function numbers are WAT function indices of this build;
offsets are from the object's start. "Not measured: aircraft in flight" marks what needs the owner's ground session.

- `BBS752.wasm`: 29,765,919 bytes, dated 2026-10-31 12:39, SHA-256
  `357292c941c563fd9d66878a96210ed16917e086547a45a2bf60f8d2d45efc3d`: the build the 2026-11-06 probe and plan 23
  measured, so the generator-4 layout's anchor and manager hold (the store's file for this build is in
  `%APPDATA%\MSFSBlindAssist\layouts\bbs757\`).
- Corrections to the probe's function list (`findings.md` §4): 22986 is not the flight controls but
  `eicas_lower`'s init (vtable slot 3); the flight controls are 23005. 22954 and 22955 are not the lower EICAS: they
  are the upper EICAS's compact engine block (RR and PW), drawn by 22931 only while the upper's byte +416 is set.
  23003 is the engine page's lower half, 23004 the STATUS page's systems block, 23007 brake temperatures and 23008
  the X-BLD cue, as the probe had them.

## Step 1: the page

### The objects

- `eicas_lower`: class `N3bbs11eicas_lowerE` (RTTI name 79108, typeinfo 79128), vptr **79068** (0x134dc). It is
  gauge 13 of the gauge manager: manager 0xf1334 + 8 × 13 = slot **0xf139c** (live 0x30e7c0), inside the FMA
  layout's 256-byte manager span. Virtuals: slot 3 (+12) 22986 init, **slot 4 (+16) 22998 the draw**, slot 5 (+20)
  1427 (called instead of the draw while unpowered), slot 6 (+24) 23035 the per-frame update (flight-control
  smoothing).
- The base `eicas` init (22884, `N3bbs5eicasE` slot 3) fills: +132 the `eicas_computer` (system 38, live 0x2b2110,
  vptr 98736), +136 the left engine (system 28, live 0x2bd040), +140 the right engine (system 29, 0x2becd0), fonts
  +156/+164/+172, and copies 72 bytes of engine limits to +192 (from 36841 on the left engine).
- `eicas_lower`'s own init (22986) adds: +272 APU (system 26), +276 hydraulics (89), +280 a failures/config object
  (system 5), +284 oxygen (88), +288 settings (1), +292 the upper EICAS (gauge 12), +296 an air-data object (32), +300
  (41); and four simulator-variable readers (Step 2, STATUS page), then writes `L:BBS_ECAM_Mode` = 1.

### Where the draw decides the page

The draw 22998 reads the page from **`L:BBS_ECAM_Mode`**, an L:var object at the static address **1093768**
(0x10b088; the function naming "BBS_ECAM_Mode", 28558, constructs it there with 4037). Its reader 23001
`(var, reset, epsilon)` returns the value truncated to an i32 and caches it as an f64 at **var+16**. The draw:

1. `this+576 = 23001(1093768, 0, 0.1)` (`i32.store offset=576`): **the page field, an i32, written every draw**.
   The upper EICAS reads it through a one-load getter, 23030 (`i32.load offset=576`), on its pointer to the lower
   (upper+272).
2. If `this+576 != 3`, the byte `this+580` (the N2/N3 red-line exceedance latch, Step 2) is cleared.
3. `br_table` on `23001(1093768, 0, 0.1) - 1` (a second read of the same variable):

| Value | Draws | Page | BlueBird's word |
|---|---|---|---|
| 1 | 23002 then 23003 | engine page: N2, N3, FF, oil, vibration (Step 2) | ENGINE (the key, manual p.102) |
| 2 | 23004, 23005, 23006, 23007 | STATUS page | STATUS (the key, and the cue word the engine page prints) |
| 3 | 23002 then 23003 | the engine page off: see "Value 3" below | (none: the ENGINE key's second press) |
| 0, any other | nothing | blank | (none) |

- **Page names** (the owner's words to confirm): value 1 "engine page", value 2 "STATUS page", value 3 and 0
  "blank". BlueBird draws no page title: the binary has no word for any page (the `ecam_mode` enum's names are not in
  it, only `ZN3bbs4lvar9set_valueINS_9ecam_modeEE…`). The words are the keys' (manual p.102 "DISPLAY ENGINE /
  STATUS"; `panel-audit-12.md`). **Value 3 is not quite blank** (below), so "Lower EICAS: blank" would still list
  FF rows. Not seen on screen (no capture, Step 2).
- **Value 3, exactly** (from the block structure of 23002 and 23003; nothing else in 22998 draws: before the
  `br_table` it only begins the frame, scales by lower+268 (1.0 live) and runs the exceedance check 23000):
  - Always: 23002's third block, ungated by the page: the "FF" label, and per engine the FF dial (a 210° arc,
    full scale 18300 lb/h or 18300 ÷ 2.205 kg/h), its needle (only while that engine is valid) and its value box.
  - Only while the latch byte +580 is set. Each draw runs 23000 first (it sets +580 when either engine's N2
    data+16 > lower+212 or, RR, N3 data+24 > lower+216), then stores the page and clears +580 unless the page is 3.
    So on page 3 it is set by an exceedance present on any page-3 draw and stays set, even after the value drops,
    until the page changes:
    23002's first block (the "N2" label and per engine the N2 dial, its digits, the X-BLD cue on a PW, and the max
    box when lower+560/+564 is not 0) and, on an RR, its second block (the same for N3, X-BLD, max box
    lower+568/+572). The digits and the max boxes are inside the gated blocks, so they show with the dials.
  - Never: everything 23003 draws (OIL PRESS, OIL TEMP, OIL QTY, VIB, the "N1" labels and the STATUS cue): its first
    test is `+576 == 3`, which returns before any drawing.
- **The keys** (cockpit model `BBS_752_BASE_INTERIOR.xml`, template `BBS_EICAS_SQUARE_BUTTON`): the left key
  (`B757_EICAS_L_STATUS_BUTTON`) writes `1 (>L:BBS_EICAS_ENGINE_PAGE_REQUEST, bool)`, the right
  (`B757_EICAS_R_STATUS_BUTTON`) `1 (>L:BBS_EICAS_STATUS_PAGE_REQUEST, bool)`, besides their animation L:var. The
  handler 36373 (EICAS computer code) consumes and clears each request:
  - ENGINE: page = (page == 1) ? 3 : 1, and the STATUS page number (static i32 at 573108, 0x8beb4) = 1.
  - STATUS: if page != 2, page = 2; else if the page number < the number of STATUS pages, page number + 1; else
    page number = 1 and page = 3.
  So: ENGINE toggles the engine page on and off (from STATUS it goes to the engine page); STATUS opens STATUS, steps
  its pages, and after the last page turns the display to 3 (engine page off). At power-up the init writes 1.
- **Live**, read in memory (var+16 and `eicas_lower+576`) and as `L:BBS_ECAM_Mode` through the MCP, always equal:

| Time | Action | Page |
|---|---|---|
| 16:41:33 | before any press | 1 (engine page) |
| 16:41:34-39 | ENGINE (`1 0 * 1 (>L:BBS_EICAS_ENGINE_PAGE_REQUEST, bool)`) | 3 at 16:41:39, latch +580 0 |
| 16:41:40-47 | STATUS (`2 0 * 1 (>L:BBS_EICAS_STATUS_PAGE_REQUEST, bool)`) | 2 at 16:41:47, page number 1 |
| 16:46:04 | ENGINE (`3 0 * 1 (>L:BBS_EICAS_ENGINE_PAGE_REQUEST, bool)`) | 1 at 16:46:04.7 (50 ms log), as found; still 1 at 16:52:47 |

  Both request variables read 0 again within 0.5 s of each press. Not measured: cold and dark, battery, ground power
  (aircraft in flight).

## Step 2: each page's items

**No capture was taken**: the camera showed the right-wing window view (a `PrintWindow` capture at 16:41 showed
only the wing and clouds), and showing the lower EICAS needs a camera change or the tablet-mirror overlay over the
cockpit screens, neither of them among the approved writes. The words, formats and blanking below are the draw's
code; the "Live" columns are the values memory held, formatted the way the code formats them, not read off a
screen. The owner's ground session should compare them with the display.

All numbers are formatted by BlueBird's own code: `f536` is `to_wstring(int)` of a value truncated toward zero
(never rounded); "1 decimal" is a fixed stream with precision 1. An engine's numbers are **blank** (an empty string,
a static `L""` in the BSS at 1008596..1008656) while that engine's **`engine+1944 == 2`** (getter 22937, "valid" is
`!= 2`; live 0 for both). The engine data block is **`engine+3576`** (getter 36839, `p0 + 3576`), all f64.

### Engine page (value 1; 23002 then 23003)

| Item, as drawn | Source | Format and blanking | Live, engine 1 / 2 |
|---|---|---|---|
| "N" "2" dial and value (label by 22891 with 2) | data+16 (RR: the IP spool; PW: N2) | whole % (trunc); blank if invalid; red at ≥ lower+212 (100.3), amber at ≥ lower+224 (98.0) | 82 / 82 (81.99, 82.09) |
| its max box | lower+560 / +564 (f32, latched by 23000 above +212) | whole %, drawn only when not 0 | none |
| "N" "3" dial and value, **RR only** (engine type, below) | data+24 (the HP spool) | whole %; red ≥ lower+216 (99.0), amber ≥ lower+228 (95.8) | 77 / 77 |
| its max box | lower+568 / +572 | as N2's | none |
| "X-BLD" on the N3 dial (RR) or N2 dial (PW) | 23008: the upper EICAS's in-flight start test (22975 on upper, engine) and the air-data value (30362 on +296) below the upper's threshold (22900) | the word only | not drawn |
| "FF" | data+88 (lb/h), or while starting a computed value (below, "FF while starting") | thousands, 1 decimal; ÷ 2.205 (kg) unless settings+596 (lower+288 → +596) is non-zero (live 1, lb); blank if invalid | 3.6 / 3.6 |
| "OIL" "PRESS" | data+96 (psi) | whole; blank if invalid | 91 / 89 |
| "STATUS" (the cue) | the STATUS list is not empty (36354: count != 0) | the word, cyan, in the 38-size font (lower+156), at (0.233 W − 140, 0.041 H + 5): about (60, 34) on the 859 × 704 display, its top left; drawn only by 23003, so only on page 1, never on the STATUS page (no STATUS-page function references U"STATUS") | not drawn (list empty) |
| "OIL" "TEMP" | data+104 (°C) | 23017: at 0 and above, whole (trunc); between −10 and 0 (exclusive), "- " then the absolute whole value (8580 is `abs`): −5.7 prints "- 5", −0.4 "- 0"; at −10 and below, the signed whole value, "-12"; **blank while that same engine's data+16 < 5** (each engine tests its own); blank if invalid | 114 / 109 |
| "OIL" "QTY" | data+112 (a fraction) | whole quarts: trunc(fraction × 21) (23014 maps 0..1 to 0..21); blank if invalid | 12 / 14 (0.5714, 0.6670) |
| "VIB" | data+120 | absolute value, 1 decimal; blank if invalid | 1.8 / 1.7 |
| "N" "1" beside each VIB value | the constant 1 (22891 draws "N" then the number as a subscript at +12.5, +8.5); not a field, not gated by engine type or validity | white, the value colour; most likely the vibration source (the real 757's VIB readout names the spool it monitors); BlueBird always draws N1 | drawn |

- **Order** (the draw's): N2 (both engines), N3 (RR), FF, then OIL PRESS, STATUS cue, OIL TEMP, OIL QTY, VIB and
  its "N1" labels. Left engine first in every row.
- **Engine type**: `engine+3768` (getter 36782, one load), 0 on this RR; the draw stores `lower+264 =
  (left engine's type == 0)` and draws the N3 block and the RR limits only while it is set. PW not measured (an RR
  was loaded).
- **Limits** (lower+192, 72 bytes copied at init from the left engine; live): +192 850, +196 795, +200 870, +204
  570, +208 108.8, **+212 100.3** (N2 red), **+216 99.0** (N3 red), +220 108.4, **+224 98.0** (N2 amber), **+228
  95.8** (N3 amber), +232 2.0, +236 4500, +240 25 (the start mark), +244 18, +248 −40, **+252 3.0** (vibration),
  +256 100, **+260 170** (oil temperature). 23002 (the dials and FF) is skipped altogether while +212 ≤ 0 (or, RR,
  +216 ≤ 0); 23003 has no such test.
- **Latches** (23000, every draw): N2 above +212 or N3 above +216 raises the max box and sets byte **+580**; RR
  vibration above +252 or oil temperature above +260 sets byte +581. The computer's byte +360 (getter 36352,
  probably the MAX IND reset) zeroes the boxes. Live: all 0.
- The dials' fuel-on start mark reads `L:BBS_fuel_RR` / `L:BBS_fuel_RR2` (36842, value 2); graphics only, no text.
- **Colours** (RGB from the static initializers 69510-69516; text brushes and width-3 pens): cyan (60, 240, 240)
  text 1171040 / pen 1171096; red (255, 0, 0) 1171048 / 1171104; amber (255, 230, 0) 1171056 / 1171112; white
  (255, 255, 255) 1171064 / 1171120; green (0, 255, 0) 1171072 / 1171128; magenta (255, 0, 255) 1171080 / 1171136.
  Labels are cyan, values white in a white box; a dial at or above amber draws its value and box amber, at or above
  red, red; X-BLD and the HYD QTY suffixes are magenta.

### FF while starting (both pages)

Per engine, with `start = 22950(engine)` = any of the engine's bytes **+6922, +6992, +6960** set, and `hold =
22971(engine)` below:

- **Engine page** (23002, third block): `start && hold` → **600**; `start && !hold` → **600 + 600 × clamp((HP − 25)
  / 35, 0, 1)** lb/h, where HP is data+24 on an RR (lower+264 set) and data+16 on a PW (23009 is the lerp `(1 − t) ×
  600 + t × 1200`, the constants 600.0 and 1200.0 stored to frame slots 416 and 408, t from `f64.const −25`, `35`,
  then 4394 clamping to 0..1); otherwise **data+88**. So 600 at HP ≤ 25 %, 1200 at HP ≥ 60 %.
- **STATUS page** (23006): `start && !hold` → **0**; `hold` (with or without `start`) → **600**; otherwise data+88.
  (The engine page shows data+88 when `hold` holds without `start`; the STATUS page shows 600 then.)
- Then both: kg conversion as above, ÷ 1000, 1 decimal, blank if invalid.
- **22971 (`hold`)** = `f64 engine+6936 < f64 engine+7104` **or** (`36794(engine)` **and** (`u8 engine+6924` **or**
  `u8 engine+6925`)). The engine's start logic 36796 sets +6922 = 1, +7056 = 2, +6936 = 0 and +7104 = 36818(engine)
  when a start sequence begins (36818: 6 s, falling linearly to 3 s as engine+4256 rises from 4257(engine+2136) to
  100), adds the frame time to +6936 while +7056 == 2, and zeroes both when the sequence ends: so the first test holds
  during a start's first phase, until its timer reaches that 3-6 s delay. +6924 and +6925 are set together by
  another branch of 36796 (cleared again 30 s later through +6952). 36794 = fuel control on (36817: RR `L:BBS_fuel_RR`
  / `_RR2` < 2, PW `L:BBS_fuelcut_PW1` / `_PW2` > 0) and the fire switch not pulled (`L:B757_ENGFIRE_1_SWITCH` /
  `_2_SWITCH` false) and not 36786 (engine+1944 == 2, or == 1 with byte +1976 clear). +6992 and +6960 are two more
  transition states the engine update 36790 sets and clears. Live, in cruise: all of these 0. Not measured during a
  start (aircraft in flight).

### STATUS page (value 2; 23004, 23005, 23006 with 23027, 23007)

| Item, as drawn | Source | Format | Live |
|---|---|---|---|
| "HYD QTY" "L" "C" "R" | hydraulics (lower+276) → +2996 array, stride 40: capacity f64 +0, quantity f64 +8 | below ("HYD QTY") | 0.98, 0.97, 1.00 |
| "HYD PRESS" | hydraulics +2896 + 8 × (L 0, C 1, R 2), f64 psi | whole | 3000, 2880, 2950 |
| "APU  EGT" (two spaces) | APU (lower+272) +1544, f64 °C | whole | −55 (APU off) |
| "RPM" | APU +1528 | whole % | 0 |
| "OIL Q" | APU +1600, rounded to quarters, clamped 0.25..1 | "FULL" at ≥ 0.99, else 2 decimals | 0.75 |
| "OXY PRESS" | oxygen (lower+284): drawn only while byte +946; value (35596 on +440): 0 while byte +456 is clear, i32 +440 while f64 +464 ≥ 1, else f64 +472 clamped to 0..(i32 +440) and rounded to tens | whole psi | 1760 |
| "RUD", "AIL", "ELEV", "AIL" | pointers only: lower+548 (rudder), +536 (left aileron), +544 (elevator, both marks), +540 (right aileron, negated): f32 −1..1, smoothed by 23035 from four simulator variables (below) | no digits; below ("Flight control pointers") | 0.000, −0.003, 0.007, 0.003 |
| "FF" | 23006: data+88, but while starting 0 (or 600 when 22971 holds), not the engine page's computed value | thousands, 1 decimal, the same units; blank if invalid | 3.6 / 3.6 |
| the status messages, then "PAGE " n | the STATUS list (below), 23027 | one message per line | none |
| "BRAKE  TEMP" (two spaces) | computer (lower+132) +584: u32[8], 0..9 per wheel; +616 the hottest wheel | below ("Brake temperatures") | 0 × 8 |

Positions below are in the draw's frame: W = lower+40, H = lower+44 (f32; live 859 × 704), y growing downward,
scaled by lower+268 (1.0 live).

#### HYD QTY

- **The number** (23019): `pct = quantity × 100 / capacity`, printed as `pct × 0.01` in a fixed stream with
  precision 2: **"0.98"**, "1.00", "0.70". Drawn white by 74289 left-aligned (its sixth argument 0), 12 px per
  character and 7.5 px either side of a ".", at (0.219 W, 0.08 H) for L, 0.136 W and 0.272 W further right for C and
  R.
- **The suffix** (23020): a separate string, empty, or **"    OF"** when `pct > 122`, or **"    RF"** when `pct <
  75` (the code compares the percentage; in the printed units, above 1.22 and below 0.75; exactly 122 or 75 has no
  suffix). It is drawn as its own item, magenta, at the **same start position** as the number: the four spaces
  advance 48 px, so "OF" / "RF" lands just after a four-character number ("0.98" is about 39 px wide). The number is
  always drawn; the suffix is beside it, never instead of it. On screen: "0.70 RF", "1.25 OF".

#### Flight control pointers

- **Rudder**: from (0.176 W, 0.591 H), a horizontal line 0.167 W long with three ticks rising 0.0075 W (at both
  ends and the middle). The pointer's x = 0.167 W × (clamp(+548, −1, 1) + 1) / 2 (23015 maps −1..1 onto 0..0.167 and
  clamps): **−1 at the left end tick, 0 the middle, +1 the right end; a positive value moves it right.** "RUD" below
  the middle, at (0.0835 W − 19, 0.014 H).
- **Ailerons and elevator**: from (0.176 W, 0.653 H), three vertical lines 0.253 H long at x = 0 (left aileron),
  0.083 W (elevator) and 0.166 W (right aileron), each with three horizontal ticks 0.022 W wide (top, middle,
  bottom). A pointer's y = 0.253 H × (clamp(v, −1, 1) + 1) / 2: **−1 at the top tick, 0 the middle, +1 the bottom;
  a positive v moves it down.** Labels below each line at 0.267 H: "AIL", "ELEV", "AIL".
  - Left aileron, one pointer on the line's left side: v = +536, the smoothed `AILERON LEFT DEFLECTION PCT`.
  - Elevator, one pointer each side: v = +544, the smoothed **negated** `ELEVATOR DEFLECTION PCT`, multiplied by 1.5
    when that negation is positive: so a positive variable moves the marks up (−1 at the top), a negative one down,
    reaching the bottom end already at −0.667.
  - Right aileron, one pointer on the line's right side: v = +540, the smoothed **negated** `AILERON RIGHT
    DEFLECTION PCT`: a positive variable moves it up.
  - So ±1 (the variables' unit is percent over 100: ±1 is ±100 %) is the scales' end ticks, except the elevator's
    lower end at −0.667. Which physical direction the simulator's signs mean (trailing edge up or down, rudder left
    or right) was not checked live: no control was moved.
- **Smoothing** (23035, every update): `new = 0.94 × old + 0.06 × input` (23036 with the f32 0.06).
- **Nothing else is printed**: the three-tick lines and the four words; no numbers, no scale words.
- **Each pointer is drawn only while its circuit is on** (the lines and words always are): 36730 → 36697 reads
  `A:CIRCUIT ON` (Bool, the reader at the system-5 object +11336) at index settings+37732 (live 0) + id, and is false
  when settings+520 < 125 (live 125). Rudder 223, left aileron 221, elevator marks 227 (right side) and 220 (left),
  right aileron 228; every variant's `systems.cfg` names them `J16_FLIGHT_CONTROLS_rudder_pos`,
  `J14_FLIGHT_CONTROLS_aileron_pos_left`, `J22_FLIGHT_CONTROLS_elev_pos_right`, `J13_FLIGHT_CONTROLS_elevator_pos_left`,
  `J23_FLIGHT_CONTROLS_aileron_pos_right`. Live (MCP): all 1.

#### Brake temperatures

- **Boxes** (23007 → 23029): origin (0.6366 W, 0.733 H); each box 0.03275 W × 0.074 H, outlined with a width-3 pen,
  its digit at the box's corner + (2, 4). Live absolute positions on the 859 × 704 display:

| Index | Box at | Group, row | As the usual 757 wheel number (index + 1) |
|---|---|---|---|
| 0 | (0, 0): (547, 516) | left group, top | 1: left gear, forward, outboard |
| 1 | (0.068 W, 0): (605, 516) | left group, top | 2: left gear, forward, inboard |
| 2 | (0.1995 W, 0): (718, 516) | right group, top | 3: right gear, forward, inboard |
| 3 | (0.2675 W, 0): (777, 516) | right group, top | 4: right gear, forward, outboard |
| 4 | (0, 0.102 H): (547, 588) | left group, bottom | 5: left gear, aft, outboard |
| 5 | (0.068 W, 0.102 H): (605, 588) | left group, bottom | 6: left gear, aft, inboard |
| 6 | (0.1995 W, 0.102 H): (718, 588) | right group, bottom | 7: right gear, aft, inboard |
| 7 | (0.2675 W, 0.102 H): (777, 588) | right group, bottom | 8: right gear, aft, outboard |

  "BRAKE  TEMP" below them at (0.0722 W, 0.1858 H): (609, 647). The screen shows two 2 × 2 groups (left and right
  gear, forward row on top, as seen from above); the last column is that reading of the layout, not traced into
  BlueBird's brake model.
- **Colours and the hottest wheel**: with v the index's value: v < 3 cyan digit in a cyan box; 3 ≤ v < 5 cyan
  digit in a cyan box, **except the wheel whose index equals computer+616 (the hottest), whose box is white**; v ≥ 5
  white digit in a white box. +616 changes nothing else.
- **"-"**: every wheel shows "-" (cyan, in a cyan box) while `A:CIRCUIT ON:379` is off (36730 with 379, as above:
  index settings+37732 + 379; `systems.cfg`: `S16_LANDING_GEAR_brake_temp`). Live: 1.

### The STATUS list

- **Where**: a `std::vector<record*>` at the static address **985716 (0xf0a74)**: begin +0, end +4, capacity +8
  (live capacity 32 entries). The computer's getter 36346 returns the constant (it ignores `this`); count is 4862
  (`(end − begin) >> 2`), entry i is `begin + 4i` (23034). Beside it: 985700 (0xf0a64) the alert list (live 1
  entry, " FMC MESSAGE", level 2) and 0xf0a84 the communication list (" ACARS", " FMC"). So the probe's "other
  vector" is right in spirit: the computer's lists are statics next to each other.
- **The record** (48 bytes, one of 164 in the static table vector at 0xf0aa4): +0 pointer to the text, **UTF-32,
  null-terminated, a constant in the data segment** (no short-string case: the text is never inline); +4 its length
  in characters; +8 the sequence stamp (u32); +12 u16 id; +14 u8 active; +16 f64 delay (s); +24 timer. Texts have no
  leading space (the alert list's advisories do).
- **Order**: newest first. The insert (36286) stamps +8 with the computer's counter (computer+348, shared with the
  alert list; live 174) and inserts with the comparator 36290 (`a+8 >u b+8`). The stamp stays on a record after it
  leaves the list.
- **Only two functions change the list**: 36286 inserts and 36318 removes. Every message goes through 36330
  `(computer, id, on)`: ids 3000 and up are status messages (table index id − 3000); `on` calls 36331 → 36286,
  `off` calls 36332 → 36318 (about 50 callers of 36330, one per system condition). 36318 finds the record in the
  list (36319), clears its bytes +14 and +15, resets its delay timer (9594 on +24) and erases it (36322), the rest
  keeping their order: **this is "Status gone"**. Nothing else writes the vector: its other users are its static
  constructor and destructor (912, 914), 36336 and 36373 (page count), 36354 (not empty) and the getter 36346, whose
  one caller is the draw 23027. There is no clear-all.
- **When**: both the insert and the remove act only while 36260(computer) holds (computer byte +384 or +385, either
  EICAS computer up); with both down the list stays as it was. The insert also returns when the computer object's
  +20 (getter 8981) is 39 (the remove has no such test), and a record with a delay (+16 > 0) is inserted only once
  its condition has held that long (its timer at +24).
- **No duplicates**: the insert returns at once when the record's active byte +14 is set (36287), and the 164
  records' texts are all different (read live), so a text is in the list at most once.
- **Paging** (23027): up to 11 entries one page; above 11, 10 per page and "PAGE " with the page number (the static
  573108, clamped to 1..pages by 36336). Lines are 38 units apart.
- **Live**: the list was empty all session (STATUS showed 16:41:44 to 16:46:04). The stamps show what came and
  went this session (highest last): the fire and overheat loop and detector messages (L/R ENG FIRE LP 1/2, L/R ENG
  OH LP 1/2, L/R STRUT OH DET 1/2, L/R TURB OH DET 1/2, APU FIRE LP 1/2, FWD/AFT CARGO DET 1/2: stamps 122-166),
  CABIN ALT AUTO 1/2 and L/R EICAS CMPTR (153-159), AIR/GND DISAGREE 167, WARN ELEX 168, NOSE A/G DISAGREE 173.
- **Raising one harmlessly** (from the code, not tried): the loop and detector messages are raised by 32919-32922,
  most likely during the fire/overheat test (their stamps cluster with no fire); L/R EICAS CMPTR (36370) while
  that EICAS computer has no power or its breaker is out (computer bytes +384/+385); status messages are raised
  only while either computer is up (36260). For the owner: on the ground, show STATUS and run the fire/overheat
  test; the power-up sequence is the other candidate.

## Step 3: power and the gate

### Power

The lower EICAS gauge callback (export `eicas_lower_gauge_callback`, 23078) runs the shared dispatcher 23059 on
gauge 13. On the draw message it calls the draw (`call_indirect` vptr+16) only while **73995(this)** holds (byte
**+4** set and byte **+5** clear) and byte **+64** is clear (a timed blank, cleared once the simulation time
passes the f64 at +72); otherwise it clears the screen to black and calls slot 5 (1427). So **the display draws while
`+4 == 1 && +5 == 0 && +64 == 0`**; while it does not, `+576` is not rewritten and must not be read as the page.
Live: +4 1, +5 0, +64 0. The upper EICAS (+0x30e580) the same. The EICAS computers: computer+384 (L) and +385 (R),
both 1. Not measured: what these bytes read unpowered (aircraft in flight); the brightness knob
(`B757_EICAS_LOWER_BRIGHTNESS`) is not part of this test.

### The gate

**The HP spool, against `TURB ENG N2:index`**, found and checked:

- **Field**: engine (lower+136 left, +140 right) → data (+3576) → **+24 on the RR** (BlueBird's N3), f64, percent.
  The intermediate N2 at +16 is exactly midway between N1 (+8) and +24 on this RR and is not in the simulator.
  On a PW the HP spool is +16 (BlueBird's N2): the draw's own start fuel-flow test picks `RR ? +24 : +16`. **PW not
  measured.**
- **Kept whatever the page**: the engine objects are systems, read by both pages and by the upper EICAS; the values
  were live on the STATUS page, with the engine page off and on.
- **Paired live readings** (memory read just before and after one MCP read, or a 50 ms memory log matched to the
  MCP values):

| Time | Memory before | Simulator `TURB ENG N2:1` | Memory after | `:2`, memory before / sim / after |
|---|---|---|---|---|
| 16:44:55 | 77.4064 | 77.4056 | 77.4040 | 77.4673 / 77.4673 / 77.4660 |
| 16:45:32 | 77.1870 | 77.1966 | 77.2400 | 77.2489 / 77.2585 / 77.3019 |
| 16:45:40 | 77.3566 | 77.3702 | 77.3914 | 77.4183 / 77.4322 / 77.4530 |
| 16:45:58-16:46:10 (log, 5 MCP reads) | | 77.3797, 77.3755, 77.3710, 77.3699, 77.3697 | | each equal to a logged memory sample to 0.00001 (both engines) |
| 16:52:53 | 77.5181 | 77.5190 | 77.5171 | 77.5795 / 77.5803 / 77.5784 |

  The simulator's value is BlueBird's +24 itself (equal to five decimals against a sample taken at the same moment);
  the differences come only from read timing. In cruise it moved at most 0.04 %/s (1 s windows over 90 s).
- **Tolerance (proposed)**: within **0.1 % N2 of any memory reading in the last 3 s** (the FMA's history rule).
  Measured worst case between a read and the nearest bracketing sample: 0.002 %. A spool-up (start, go-around) moves
  N2 by several %/s, which the 3 s history covers; not measured (aircraft in cruise).
- **Rejected**: oil pressure, oil temperature and vibration match the simulator on engine 2 (89.85 psi, 109.48 °C,
  1.693) but BlueBird adds fixed offsets on engine 1 (+2.00 psi, +5.00 °C, +0.10); oil quantity does not match
  (BlueBird 0.571 / 0.667, the simulator 100 %); FF does not (3600 lb/h against the simulator's 130).
- **A second check, available**: `eicas_lower+576` must equal `L:BBS_ECAM_Mode` while the display draws (equal in
  all four live states).

### Simulator variables the draws take

The lower EICAS's code reads: `L:BBS_ECAM_Mode` (the page), `L:BBS_fuel_RR` and `L:BBS_fuel_RR2` (the dials'
start mark), `A:RUDDER DEFLECTION PCT`, `A:AILERON LEFT DEFLECTION PCT`, `A:AILERON RIGHT DEFLECTION PCT`,
`A:ELEVATOR DEFLECTION PCT` (percent over 100, at lower+344, +376, +408, +440, smoothed into +548, +536, +540,
+544), and **`A:CIRCUIT ON` at six indexes** (found in the follow-up: 223, 221, 227, 220, 228 for the STATUS page's
pointers and 379 for its brake digits, live all 1), plus, during a start only, `L:BBS_fuelcut_PW1`/`_PW2` and
`L:B757_ENGFIRE_1_SWITCH`/`_2_SWITCH` through the FF test (22971). Its init also builds readers for `A:HYDRAULIC
PRESSURE` (psi) and `A:HYDRAULIC RESERVOIR PERCENT` (lower+472, +504) that no draw reads. Every drawn number has a
memory source; the six circuits have none (each draw asks the simulator). So the reader registers the gate's
`TURB ENG N2:1` and `:2`, optionally `L:BBS_ECAM_Mode` for the second check, and, to show the brake "-" and the
pointers' absence as drawn, `CIRCUIT ON:379` and the five pointer circuits: all of these would make 9, one over the
8 allowed, so one choice has to give (for example the second check, or the pointers' circuits, the pointers having
no words to speak anyway).

## For the layout (Task 2)

Proposed parts (required: the objects, the page and the gate; optional: each page):

- **Objects**: the FMA layout's anchor, manager base and span; the `eicas_lower` vptr (79068) found in the manager;
  lower → computer +132, engines +136 / +140, APU +272, hydraulics +276, oxygen +284, settings +288.
- **Page (required)**: page field +576 (i32); its values {1 engine, 2 STATUS, 3 engine off}, any other blank;
  power bytes +4 (set), +5 (clear), +64 (clear); the N2/N3 latch byte +580 (on value 3 the dials show only while
  set). Optionally the page variable's object (1093768, f64 +16) for the second check.
- **Gate (required)**: engine → data +3576; engine type +3768 (0 = RR); HP spool +24 (RR) or +16 (PW); compared
  with `TURB ENG N2:1` / `:2`.
- **Engine page (optional)**: data fields N2 +16, N3 +24, FF +88, oil pressure +96, oil temperature +104, oil
  quantity +112 (× 21, quarts), vibration +120; valid `engine+1944 != 2`; the start flags +6922, +6992, +6960 and
  the FF hold test's fields (+6936, +7104, +6924, +6925, and 36794's inputs) or, simpler, FF read as "not read while
  starting"; the limits block +192 (N2 red +212, N3 red +216, N2 amber +224, N3 amber +228, vibration +252, oil temperature +260);
  max boxes +560, +564, +568, +572; units settings+596; the words "N2" (drawn "N" "2"), "N3", "FF", "OIL PRESS",
  "OIL TEMP", "OIL QTY", "VIB", "STATUS", "X-BLD"; FF ÷ 1000.
- **STATUS page (optional)**: the list 985716 (begin +0, end +4); record text +0, length +4, sequence +8; the page
  number 573108; 11 lines, or 10 and "PAGE n"; hydraulics +2896 (stride 8) and +2996 (stride 40: +0 capacity, +8
  quantity; suffix thresholds 122 and 75 %); APU +1544, +1528, +1600; oxygen +946, +440, +456, +464, +472; flight
  controls lower+536, +540, +544, +548 (with the signs and the elevator's 1.5 above) and their circuit ids; brakes
  computer+584 (u32[8], the drawing order and positions above) and +616, circuit id 379; settings+37732 (the
  circuit base) and +520; the words "HYD QTY", "HYD PRESS", "APU  EGT", "RPM", "OIL Q", "FULL",
  "OXY PRESS", "RUD", "AIL", "ELEV", "BRAKE  TEMP", "PAGE ", "OF", "RF".

## For the recipe (Task 3)

- **eicas_lower**: the vptr of class `N3bbs11eicas_lowerE` (RTTI, as the FMA recipe finds `N3bbs3pfdE`'s); the
  manager slot holding it (scan the span, as for the PFD).
- **The draw**: the vtable's slot 4 (vptr+16 → table index → function), or the one caller of the function
  referencing U"BRAKE  TEMP". In it: the call whose first argument is a constant L:var object and whose result is
  `i32.store`d to `this` gives the page field (+576); the constant is the object the function naming
  "BBS_ECAM_Mode" constructs (`TryVariableField`'s first steps). The `br_table` on that variable − 1 gives the
  values: the case whose callees reference U"HYD QTY" and U"BRAKE  TEMP" is STATUS; the case calling the function
  that references U"OIL" and U"VIB" is the engine page, and of its two values the one that function compares
  (`i32.eq` with the `i32.load offset=576` at its start, then returns) is "engine off"; the default is blank. A
  check: the upper EICAS's one-load getter 23030 loads the same offset.
- **Power**: in the dispatcher the gauge callbacks share (23059), the predicate called with `this` before
  `call_indirect` of vptr+16 loads two bytes (`load8_u` +4 set, +5 clear), and the `load8_u offset=64` tested next.
- **Engines and the gate**: the base `eicas` init (vtable slot 3 of `N3bbs5eicasE`) stores the two engine objects
  at +136 / +140; the one-add getter (`p0 + 3576`) every engine read goes through gives the data block; in 23002's
  first dial loop the `f64.load` after it is N2 (+16), in the second (RR) loop N3 (+24); the FF fallback's `select`
  on the RR flag picks between them (the HP spool, the gate field). The RR flag is the `i32.eqz` of the one-load
  getter (+3768) the draw stores to `this+264`.
- **Engine page fields**: in 23003 the `f64.load` offsets after the data getter, told apart by the UTF-32 label
  each block draws before its values ("PRESS", "TEMP", "QTY", "VIB"); "FF" in 23002.
- **The STATUS list**: in the only function referencing U"PAGE " (23027), the getter returning an `i32.const`
  (985716), passed to the size function (`(load +4 − load +0) >> 2`) and the element function; the record's text
  is the `i32.load` the drawn string comes from; the comparator `+8 >u +8` (36290) gives the order; the page number
  is the global the page getter (36336) loads.
- **STATUS page fields**: in 23004 the one-load getters called on lower+272 (APU: +1544 after "APU  EGT", +1528
  after "RPM", +1600 after "OIL Q") and on lower+276 (hydraulics); in 23007 the computer getters +584 and +616.
- **Circuit ids**: each 36730 call takes a pointer to a frame slot the caller has just stored an `i32.const` to:
  in 23005 one per pointer, told apart by the label the following scale call (23026) draws and by the translate
  before it; in 23007 the one before the wheel loop (379). The variable is the one the function naming "CIRCUIT ON"
  constructs into the system-5 object at +11336.
- **Brake positions**: 23007's eight `23029(ctx, pos, index)` calls with their constant index and the position each
  passes (multiples of lower+40 and +44 by `f32.const`).
