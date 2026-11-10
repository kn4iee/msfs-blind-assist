# The upper EICAS from memory: what plan 24 reads and gates on (plan 24, Task 1)

Measured 2026-11-09, about 18:50 to 21:30 local, MSFS 2024 (1.9.24.0), the BlueBird 757 "BlueBird 757-200 RR N/W 4
Door" (Rolls-Royce RB211) at the gate, BBS Nest running, MSFSBA closed. Memory was read only
(`PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ`, the private b757-memory probe scripts and scratchpad scripts
built on them); no snapshot was kept. Writes to the simulator, at the owner's go-ahead, through the calculator path
with the app's own codes: CANC and RECALL (`L:B757_MAIN_CANCEL_BUTTON`, `L:B757_MAIN_RECALL_BUTTON`), the engine fire
test (`L:B757_PED_FIRE_ENG_BUTTON`, held about 2.5 s), the packs OFF, and both engine starts (the owner: "just start
the engine yourself"; start selector GND, `BBS_fuel_RR2`/`BBS_fuel_RR` to RUN at 25 % N2). Both engines were left
running at idle, packs OFF. The draw was traced offline from the WAT of the same build; every route and field below was
then checked live. Function numbers are WAT function indices of this build; offsets are from the object's start.
Screen captures: `PrintWindow` of the 3840x2160 captain's view, the upper EICAS cropped at 2900,1390 (440x380).

## Step 1: the message lists

- **Build:** `BBS752.wasm` 29,765,919 bytes, 2026-10-31 12:39:12, SHA-256 `357292c9…45efc3d`, as plan 23 measured; the
  installed generator-4 layout (code hash `39462c4a…`) fits it.
- **Roots:** two managers. The gauges manager 0xf1334 (`f960`, guard byte 0xf142c): slot 12 → `[0xf1394]` =
  `eicas_upper` (vptr 0x133e0; live 0x30e580). The systems manager 0xf0f20 (`f963`, guard byte 0xf1330): slot 38 →
  `[0xf1050]` = `eicas_computer` (vptr 0x181b0; live 0x2b2110), slots 28/29 → `[0xf1000]`/`[0xf1008]` = the engines
  (left 0x2bd040, right 0x2becd0), 41 → `[0xf1068]` tmc, 12 → `[0xf0f80]` tat_probe, 112/113 → `[0xf12a0]`/`[0xf12a8]`
  the thrust reversers. `eicas_upper` keeps them: +132 computer, +136/+140 engine L/R, +272 eicas_lower, +308/+312
  tr L/R, +316 tmc, +328 tat_probe (setup 22884/22903; all checked live).
- **Lists:** the draw (22920, `eicas_upper` vtable slot 4) calls 36345 → `i32.const` 0xf0a64, the **main list**, and
  36347 → 0xf0a84, the **communication list**; each a `std::vector<entry*>` (begin, end, cap). Both getters ignore their
  argument: the lists are statics. Definitions: 0xf0a94 (278 entries of 56 bytes), status definitions 0xf0aa4 (164 of
  48 bytes; message numbers ≥ 3000).
- **Entry (56 bytes):** +0x00 UTF-32 text pointer (the definition's literal, null-terminated), +0x04 length, +0x08
  posting sequence (computer+348, +1 per post), +0x0c u16 id, +0x0e u8 posted, **+0x0f u8 cancelled**, +0x10 f64
  posting delay, +0x18 timer, **+0x30 i32 level**.
- **Levels** (switch in 22920 ;635–;673, posting 36259): 4 **warning**, red (15 definitions: ENGINE FIRE, APU FIRE,
  AUTOPILOT DISC, CABIN ALTITUDE, …); 3 **caution**, amber, no indent (42); 2 **advisory**, amber, one leading space
  (214); 1 **communication**, white, one leading space (7: " ATC", " CABIN ALERT", " CABIN CALL", " FMC",
  " GROUND CALL", " SELCAL", " ACARS"), on the communication list; 0 none defined (posted like 1). **Status messages are
  never drawn on the upper EICAS** (their own table; plan 25's, S11).
- **Live examples:** the fire test posted R/L ENGINE FIRE (4), R/L ENG OVHT (3), " FIRE/OVHT SYS" (2), all gone about
  2 s after release; R/L ENG SHUTDOWN (3) at engines off, gone at each engine's fuel on; " R PACK OFF"/" L PACK OFF"
  (2) when the packs went OFF; " R ENG BLEED OFF"/" L ENG BLEED OFF" (2) after each start; " PARKING BRAKE",
  " PROBE HEAT", " C HYD ELEC 2" (2). No communication message was raised (the cockpit's CALL buttons call the cabin):
  that list's shape is from the code only; Task 8 reads one if one comes.
- **Posting** (not drawn, decides the list): nothing posts unless computer byte +384 or +385 is set (the EICAS
  computers' power, loads 213/233); the posting delay (+0x10) must elapse.

## Step 2: the order and the pages

- **Order:** the main list is sorted by comparator 36268: level descending, then sequence (+0x08) descending, so
  warnings newest first, then cautions newest first, then advisories newest first. Live: the packs switched OFF a
  second time came back as sequence 68/69 (18/19 before) and the bleed messages after the starts as 86/87, above
  them. The communication list is in posting order (`push_back`). The screen draws the main list top down in vector
  order, skipping cancelled entries (captures: the list order on screen every time).
- **Lines:** 10 lines of the main list when the communication list holds 0 or 1 message; 11 − n when it holds n ≥ 2.
  Line pitch 38 from y = height × 0.115; line 10 (the 11th, y + 380) is the bottom line.
- **Communication list:** drawn white from the bottom line up (entry 0 on the bottom line); while RECALL shows, entry
  0 is not drawn and its line is left to RECALL/PAGE.
- **Page:** computer+344 (i32, 1 at construction; live 1). Nothing selects a page by index: the screen shows the
  first `lines` uncancelled entries; the number is display only. `PAGE n` is drawn (white, x = 45, the bottom line)
  only when the communication list is empty or RECALL shows, and only if (page = 1 and the drawn count = lines) or
  (page ≠ 1 and drawn ≠ 0). Format `U"         PAGE  %d"`: 9 spaces, `PAGE`, 2 spaces, the number. **No "of n".**

## Step 3: cancel and recall

- **Read:** eicas_computer's update 36369 (only the computer whose id is not 39) reads the two buttons with
  `get_named_variable_value`; a 1 is taken once (latched) and the module writes 0 back about 0.1 s later itself. CANC
  is handled before RECALL (`if … else if`).
- **CANC (36374):** marks +0x0f = 1 on the uncancelled non-warning entries in list order, at most `lines` of them;
  if it marked `lines`, page += 1. **Warnings are never cancelled**; communication messages are not touched.
- **RECALL (36375):** page = 1, every main-list entry +0x0f = 0, computer+352 (f64) = the clock (static f64 at
  0x1244e8, the module's unpaused time in seconds). `RECALL` (white, bottom line) is drawn while clock − computer+352
  < 0.5 s.
- **A cancelled message stays in the vector** with its mark; an entry leaves only when its condition clears (36306,
  which also clears +0x0e and +0x0f).
- **Live:** CANC held over a module update marked all five advisories (+0x0f = 1, page stayed 1: five < 10) and the
  screen showed no message; RECALL cleared the five marks, the screen showed all five again, and computer+352 took the
  clock (4023.89; the clock read 4096.29 72 s later). A first CANC sent as press and release in one batch was never
  seen: the 1 must last across a module update (the app's presses hold it). The 0.5 s `RECALL` word was not caught by
  a capture.
- **Other computer fields:** +348 posting counter (87 live), +360 byte `L:B757_EICAS_MAX_IND_BUTTON` pressed this
  update, +364 the blanking countdown (Step 5).

## Step 4: the rest of the screen

- **Engine values** (from the engines, not from `eicas_upper`): `f36839(engine)` = engine+3576, f64: +0 **EGT** °C,
  +8 **N1** %, +56 **EPR**, +80 commanded EPR (+16 and +24 read 35.1 and 50.35 live on the RB211: its N2 and N3, the
  sim's N2 being the RB211's N3). N1 alternate engine+4104 when byte +6922, +6960 or +6992 is set. Validity:
  engine+1944 ≠ 2 (set by the failure manager; 2 blanks the readouts). Type engine+3768: 0 RB211, 1 PW2000.
  Live, both engines idle: EGT 357.84/360.68, N1 19.930/19.930, EPR 1.0300; the screen 358/360 (later 357/360),
  19.9/19.9, 1.03/1.03. (`eicas_upper`+0x6a8 holds a separate per-engine block, EPR/EGT/N1/N2 at stride 0x20, which
  the draw does not read: not used.)
- **Formats:** EPR fixed 2 decimals, white, never amber or red; N1 fixed 1 decimal; EGT `trunc`, an integer
  (max(0, EGT)). Blank: invalid engine (+1944 = 2); on the PW2000 also N1 ≤ 5 (N1) and N2 ≤ 5 (EGT, EPR).
- **Colors** (S10: decided from readable fields): limits are f32 copies of engine_L+3920 at eicas_upper+192..+263:
  +192 EGT red (850.0 live), +196 EGT amber (795.0), +204 EGT start limit (570.0), +208 N1 red (108.8), +220 N1 amber
  (108.4). N1 red if N1 ≥ red; else amber if N1 ≥ amber unless (RB211 and the mode inhibit); else white. EGT red if
  ≥ red; else amber if ≥ amber unless the mode inhibit; else white. Mode inhibit: TMC valid and tmc+452 ∈ {0, 1, 2, 3,
  11}. Max-exceedance memories (f32, white, shown when ≠ 0): eicas_upper+336/+340 N1 L/R, +344/+348 EGT L/R, zeroed by
  `L:B757_EICAS_MAX_IND_BUTTON` (0 live).
- **Thrust reference** (22929; tmc = eicas_upper+316): word from tmc+452 (live 0, drawn `TO`): 0 `TO` (`D-TO` when D-TO:
  TMC valid and bytes tmc+3096 and tmc+3228), 4 `CLB`, 5 `CLB1`, 6 `CLB2`, 7–9 `CRZ`, 10 `CON`, 11 `G/A`; 1, 2, 3 and
  ≥ 12 draw nothing. A number from tmc+456 after TO/D-TO and CRZ only: 1 → "1", 2 → "2". Manual
  (`L:BBS757_THRREF_PULL`) overrides all: `MAN`, no number, no derate. Derate text (green, above the word, then a
  small green `c`) only when byte tmc+3016 and TMC valid: tmc+3092 (i32) 0 → "0", < 10 → "+ n", ≥ 10 → "+n". Hidden
  whole while either reverser is fully deployed, or unless (manual or TMC valid). Reference EPR (green, 2 decimals,
  above each dial): tmc+480 f64 (live 1.7073, drawn `1.71`), or the manual values tmc+2976 (L) / +2984 (R) with
  `L:BBS757_THRREF_PULL` set.
- **TAT** (22930): tat_probe+344 f64 °C (live 11.992), set each update from the simulator's `TOTAL AIR TEMPERATURE`
  (celsius) except while its ground-heating model eases it. Text: v = trunc; −10 < v < 10: "+ v" or "- |v|"; else
  "+v" or "v" (e.g. "+15", "-15"); then a small `c`. Drawn `TAT +11c` live. No blanking or color change.
- **REV** (22928): tr+664 i32 (reversers at eicas_upper+308/+312): 1 moving → `REV` amber, 2 fully deployed → `REV`
  green, 0 → nothing (live 0, 0).
- **TAI**, **in-flight start envelope**, the cyan **V cue**, **TMC valid**: see "Follow-up" below.

## Step 5: power and the gate

- **Dark** when any of: the gauge's byte eicas_upper+4 (powered) clear, +5 (failed) set, +64 (timed blank) set (the
  dispatcher 23059 then fills black); or computer+364 ≠ 0 (25 computer updates after `L:B757_ECAM_LRAUTO_KNOB` moves
  into or out of position 2). Live, engines running: +4 = 1, +5 = 0, +64 = 0, +364 = 0. +4 follows load 214
  (`CIRCUIT ON:214`, the upper display breaker `L:BBS_CB_BITS_6` mask 2097152), with a random 10–20 s power-up delay
  after a cold start. Compact mode (eicas_upper byte +416, load 234 lost: the lower display) is not dark: the lower
  engine page replaces the in-flight start block, EGT is not drawn.
- **The gate's value (S3):** N1, engine+3584, against `TURB ENG N1:1`/`:2` (percent). Recorded on one clock (memory
  and the simulator, about 2 Hz, Python-SimConnect beside the read) through both engine starts:
  - steady idle: |memory − simulator| ≤ 0.0002 % (the engine field), so the field is the simulator's N1;
  - while N1 rises, memory leads the simulator by about 0.5 s (within the 3 s history);
  - at light-off the simulator's own N1 jumps for about 1 s (right: 33.2 % while BlueBird's stayed at 26.3 %; left:
    32.7 % against 26.2 %). Against the 3 s history, ticks failing per start: tolerance 2.0 % → about 1.0 s; 2.5 % →
    0.5 s; 3.0 % → 0.5 s; the monitor stops only after 2 s of failing (`MismatchToleranceMs`).
- **EGT and EPR have no simulator counterpart:** the simulator's EGT read 351 °C at idle against BlueBird's 361, and
  223 °C when BlueBird's read 87 during the start; its EPR stayed 1.000 while BlueBird drew 1.03. BlueBird's
  `L:B757_RR_START_N1_R` (20.0023 at idle) is RR-only: not used.

## Follow-up: TAI, the in-flight start envelope, TMC valid, the V cue

- **TAI** (inside 22951): the pneumatic system (systems id 18, slot `[0xf0fb0]`, eicas_upper+276) keeps a
  `std::vector` of inline 184-byte components at pneumatic+1932 (begin) / +1936 (end); a component's id is the i32 at
  +4 (36 components, added in a fixed order: live ids 30, 31, 16, 17, …). Id **16** (element 2) is the left engine
  anti-ice valve, id **22** (element 9) the right, driven by `L:B757_OVHD_ANTI_ICE_L/R_ENG_BUTTON`. +72 f64 = valve
  position, % open (50 %/s), +52 i32 = state (0 at target, 1 moving, 2 closed). `TAI` is drawn green above that
  engine's N1 box when +72 ≥ 7 and +52 is 0 or 1. Live: left anti-ice ON → +72 100.00, +52 0, the screen showed `TAI`
  over the left N1; OFF → 0.00, 2, gone.
- **In-flight start envelope** (22932, magenta): drawn when all hold: fms (eicas_upper+284, slot `[0xf1030]`) byte
  +1513 = 0 (it is `SIM ON GROUND`, written each FMS update; live 1); the lower display powered (load 234; not compact
  mode); eicas_lower+576 = 1; and for L or R: engine byte +3992 = 0 (it is `ENG COMBUSTION:n` and the engine not
  failed; live 1, 1) and that engine's fire switch clear (fire_protection = eicas_upper+288, byte +400
  `L:B757_ENGFIRE_1_SWITCH`, +401 ENGFIRE_2; live 0, 0). Rows: 3 × 36 bytes at eicas_upper+420: +0 FL, +4 min kt,
  +8 max kt (i32; FL 0 = unused row; live all 0). The rows go stale while the gate is false, so a reader tests the
  gate itself. Text: the title `IN-FLT START`, then per used row `FL`, the FL right-aligned in 3 (" 50", "250"),
  `<min>-<max>`, `KTS`.
- **TMC valid** (the mode inhibit's and the thrust reference's): byte tmc+408 = 0 and (channel L ok or channel R ok).
  Channel L ok: byte tmc+440 = 0, `L:BBS_CB_BITS_4` bit 29 (CB 158) clear, and `CIRCUIT ON:(bbos+37732 + 158)`;
  channel R: tmc+441, bit 30 (CB 159), `CIRCUIT ON:(… + 159)`; every circuit reads off while i32 bbos+520 < 125. No
  memory field holds the circuit state (the index base bbos+37732 is a runtime value). Live: +408, +440, +441 all 0.
- **The cyan V cue** (`"  V V V V V V V"`, 22920 ;1382–;1503): drawn when eicas_upper+416 = 0 and eicas_lower+576
  (`L:BBS_ECAM_Mode`) = 1, or = 3 with eicas_lower byte +580. Mode 1 is the lower ENGINE page (N2, N3 on the RB211,
  FF, oil, VIB), 2 the STATUS page, 3 the engine page deselected (FF drawn, the N2/N3 dials only after an exceedance);
  +580 latches an N2/N3 exceedance (23000), cleared whenever the mode is not 3. So the cue shows exactly while the
  powered lower EICAS draws its N2/N3 dials. Nothing in the code names it.

## For the recipe (Task 3)

Each step from the export, numbered for this build:

1. Export `eicas_gauge_callback` = 23077 → `f960` (`i32.const` 987956 = gauges manager 0xf1334) → f23058(mgr, 12) →
   23060 → 5105 (`base + id << 3`) → `[0xf1394]` = eicas_upper; its draw = vtable slot 4 = 22920 (vtable 0x133d8,
   vptr 0x133e0).
2. 22903's first call 22884 (base setup; 22903 is the setup, vtable slot 3, beside the draw 22920 at slot 4): `f963`
   (`i32.const` 986912 = systems manager 0xf0f20) → f22885(mgr, 38) →
   `[0xf1050]` eicas_computer stored at +132; f22886(mgr, 28 / 29) → `[0xf1000]`/`[0xf1008]` stored at +136/+140.
3. 22903 stores the rest: 112/113 tr → +308/+312; 41 tmc → +316; 12 tat_probe → +328; 13 (gauges) eicas_lower → +272.
4. Lists: in 22920 the callees on `i32.load offset=132` whose body is one `i32.const`: 36345 → 0xf0a64 (the first, its
   result feeding the first loop), 36347 → 0xf0a84 (the second).
5. Computer getters: 36335 → +344 page; 36351 → +352 recall time vs clock f64 [0x1244e8], < 0.5; 36353 → +364 ≠ 0.
6. Entry getters: 22923 → byte +15 cancelled; 22924 → i32 +48 level; 22925 → +0 text pointer.
7. Thrust reference (22929 on +316): 46946 → +452; 22935 → +456; 22936 → +3092; 46989 → bytes +3096/+3228; 46991 → byte
   +3016; 46956 → f64 +480; 47052/47053 → f64 +2976/+2984.
8. Engine values (on +136/+140): 36839 → +3576 block (+0 EGT, +8 N1, +56 EPR); 36849 → +4104; 22937 → +1944 ≠ 2;
   36782 → +3768 type; 36841 → +3920 limits, copied to eicas_upper+192..+263.
9. 32530 → tr+664; 35206 → tat_probe+344.
10. TAI: f47114 on eicas_upper+276 (pneumatic) → +1932 vector, 184-byte stride, id at +4 (16, 22 as `i32.const` in
    22951 ;1009/;1022), +72, +52. Envelope: 22956's reads (fms+1513 on +284, engine +3992, fire_protection +400/+401 on
    +288, eicas_lower+576 on +272), rows at +420 (22959, stride 36). TMC valid: f46943 → tmc+408, +440, +441.
11. Words: the level colors from the switch in 22920; the thrust words from the UTF-32 constants 22929 draws; the
    `PAGE` format and `RECALL` from their UTF-32 literals in 22920.
