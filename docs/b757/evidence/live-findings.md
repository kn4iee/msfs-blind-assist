# BBS 757 live probe notes (2026-10-25)

MSFS 2024 1.9.21.0 (Microsoft Store), package `bluebirdsim-aircraft-757-200` version 0.1.0 (`manifest.json`, built
2026-10-21), loaded as "BlueBird 757-200 PW N/W" (ATC type `BBS752PW`, registration G-BBS75). MobiFlight event module
1.0.1; MobiFlight Connector running (it does not serve port 8320). Probed through the simconnect MCP (MobiFlight
calculator path and native SimConnect), the Coherent debugger (:19999) and window captures. Every value changed was
put back unless noted at the end.

## Session 1: KSEA gate, cold and dark (about 00:25 to 00:55 local)

- The cockpit model took every L:var write (the overhead capture showed the APU selector where it was written), but
  BlueBird's systems did not react to any of them for the whole session: battery ON (calculator path, twice, with a
  fresh 0 → 1 transition), standby power to BAT, APU START, and the WASM's own `L:BBS_APU_INSTANT_START`. The battery
  bus (`ELECTRICAL MAIN BUS VOLTAGE:3`) stayed at 0 V. The WASM was running (it publishes
  `L:B757_M730_BATT_PERCENT` 100) and sim time ran at real-time rate.
- At the start of the session the Coherent debugger's port was held but did not answer; the owner killed some
  processes, after which it answered.
- **Cause, found afterwards: BBS Nest was not running.** BlueBird's companion app `C:\BBS Nest\bbs_nest.exe` (its
  own SimConnect client, `SubSystem.dll`; it renders the ND terrain picture and feeds traffic over SimConnect client
  data) started at 00:55:33, after this session ended and just before the reload; the sim had run since 00:25:45. The
  module carries a message that the aircraft systems do not run without BBS Nest (`wasm-findings.md` §1). A blind
  pilot gets that message only on a display, so the app must check for the process itself and say so.
- Annunciator read-back trap: the battery ON light's visibility also needs `A:CIRCUIT ON:288`, which is the overhead
  panel lighting circuit, fed only from the left AC bus (`systems.cfg` circuit.288 → bus.9). With battery power alone
  no annunciator can show, so a dark light proves nothing until AC is up.

## Session 2: reloaded on a runway (about 00:58 local)

- **The aircraft loads cold and dark on a runway too**: battery off, all buses but the hot battery buses (1, 2) at
  0 V, engines and APU stopped. BlueBird applies its own panel state at load, whatever the sim's start position.
- **Switch writes work after the reload, through both routes.** Battery ON by native SimConnect L:var write: after
  8 s buses 3 (battery), 5 (DC standby) and 8 (AC standby) read 30.6 V. Battery OFF through the MobiFlight calculator
  path: 0 V after 8 s; ON through the calculator path: 30.6 V after 8 s. The reaction is not immediate (a relay or a
  slow WASM tick): a read-back must wait several seconds.
- **APU start through the calculator path works**: selector `L:B757_OVHD_APU_SWITCH` 2 (START) for 3 s, then 1 (ON).
  `L:B757_APU_STATE` went to 1 and `L:B757_APU_RPM_PRC` climbed (14.8 % → 19.8 % → 31.8 % over about a minute),
  realistic spool-up. **The stock `APU PCT RPM` read 100 % at once while BlueBird's was at 19.8 %**: stock APU values
  are not usable on this aircraft; use `L:B757_APU_RPM_PRC`. It reached 99.6 % after about 90 s; with the APU
  generator switch already ON (its default), the left and right AC buses (9, 11) read 32 V.
- **Both CDUs then came up and their screens arrived as text** over the Comm Bus; key presses work as held L:var
  pulses. Details in `cdu.md`.

## MCP (session 2)

- The selected values live in WASM L:vars: `L:BBS_AP_TARGET_ALTITUDE` (10000, matches the window),
  `L:BBS_AP_TARGET_HEADING` (0), `L:BBS_AP_TARGET_VS`. No IAS target var found yet (the window showed 200; stock
  `AUTOPILOT AIRSPEED HOLD VAR` read 100; `L:BBS_TARGET_MACH` exists): find it in the panel sweep.
- The heading knob's cockpit code steps a knob counter and fires `K:HEADING_BUG_INC`/`DEC`; the WASM counts those
  events: one stock `HEADING_BUG_INC` moved BlueBird's target 0 → 1 and the stock bug 0 → 1.
- **Stock `HEADING_BUG_SET` does not reach BlueBird**: it set the stock bug to 90, BlueBird's target stayed at 1.
- **A direct write of `L:BBS_AP_TARGET_HEADING` sets the MCP heading exactly**: 90 written, the next INC gave 91, and
  the MCP heading window showed `091` (window capture, cropped). So exact MCP values can be set by L:var write, if the
  same holds for altitude, V/S and speed (to measure); the stock bug then disagrees and must not be read as the MCP
  value.
- Restored afterwards: target heading 0, stock bug 0.
- **Speed (session 3)**: the MCP IAS target is `L:BBS757_ORANGE_AIRSPEED_BUGS` (read 204 while the MCP window
  showed 204 in a capture; found by diffing all WASM names before and after five `K:AP_SPD_VAR_INC`). It is an
  output: a direct write of 210 was overwritten (the next INC gave 205). Each `K:AP_SPD_VAR_INC/DEC` is 1 kt, and a
  burst of 10 or 11 events in one calculator call all counted. So the app sets speed by stepping the difference.
  The stock `AUTOPILOT AIRSPEED HOLD VAR` moves with the steps but sits at an unrelated offset (100 against 200):
  never read it as the MCP value. The window's digit drums (`B757_MCP_Gau_SPD_AUTO_1/10/100`) are cumulative
  animation positions, not digits.
- **Altitude (session 3)**: `L:BBS_AP_TARGET_ALTITUDE` adopts a direct write (12000 written, one `K:AP_ALT_VAR_INC`
  gave 12100: a step is 100 ft). Restored to 10000.
- **V/S (session 4, 2026-10-25 overnight, aircraft powered at the gate)**: `L:BBS_AP_TARGET_VS` adopts a direct write,
  negative values too (500 and -1500 read back), and one `K:AP_VS_VAR_INC` added 100 fpm (500 → 600). Restored to 0.
  Reads through the simconnect MCP lag one call behind its writes (its own registration), so read twice.
- **Mach flag (session 4)**: pulsing the IAS/MACH changeover (`L:B757_MCP_AP_SPD_MACH_SW` 1 then 0, separate calls)
  left `L:BBS_AIRSPEED_IN_MACH`, `L:BBS_TARGET_MACH`, stock `AUTOPILOT MANAGED SPEED IN MACH` and the speed target
  (200) unchanged: either the pulse did not register or the flag is another variable. Unproven.
- **Fuel and payload setters (session 5, 2026-10-25 morning, on the ground, APU running)**: every
  `L:BBS_EFBX_SET_*` setter rests at -1, takes one write and is consumed. Fuel: `_FUEL_LEFT_LBS` 10000 and
  `_FUEL_RIGHT_LBS` 10000 gave 1,488.5 gal per wing tank, `_FUEL_CENTER_LBS` 20000 gave 2,973 gal (BlueBird converts at
  about 6.72 lb/gal; the sim reports 6.70); total 39,865 lb. Tanks (`flight_model.cfg`): center 6,924 gal, left and
  right 2,176 gal each. Payload (stations `PAYLOAD STATION WEIGHT:3` First Class, `:4` Economy, `:5` Baggage FWD, `:6`
  Baggage AFT; `:1`/`:2` are the pilots at 200 lb): `_PAX_BUSINESS_LBS` 1900, `_PAX_ECONOMY_LBS` 30000,
  `_CARGO_FWD_LBS` 4000, `_CARGO_AFT_LBS` 6000 each set their station exactly. `_PAX_WEIGHT_LBS` 32000 split itself
  2,909.09 / 29,090.9 (1 : 10, BlueBird's cabin ratio). `_CARGO_WEIGHT_LBS` fills the forward hold first: 8000 went
  all forward, 25000 went 10,300 forward and 14,700 aft (the hold limits, it seems). Left loaded: fuel 39,865 lb,
  passengers 32,000 lb, cargo 25,000 lb.
- **BlueBird's own SimBrief loading (session 5, EFB by scripted clicks, seen through the ND mirror)**: the plan was
  BVI 4RK KSEA-KDEN, units lbs, block fuel 25,550, 224 passengers at 175 lb (+55 lb bags), cargo 12,259, payload
  51,459, ZFW 184,000 (= max ZFW). EFB positions (EFB-local): Aircraft app menu FUEL (67,237), WEIGHT & BALANCE
  (124,307); Fuel page IMPORT FROM SIMBRIEF (508,962; 511,981 missed); START BOARDING in the top bar (1231,24); the
  "Current fuel exceeds the flight plan fuel. Are you sure you want to defuel?" dialog YES (400,648), the same spot
  as the units dialog's YES; "BOARDING IS COMPLETE!" OK (731,879). The Fuel page's import and START BOARDING both ask
  that question when current fuel is above the plan's.
  - Fuel (IMPORT FROM SIMBRIEF, then the defuel ran about 3.5 minutes): **both wings evenly, center empty** for a
    block under the wings' capacity: left 1,902.0 gal, right 1,902.7 gal (about 12,745 lb each), center 0; block
    25,550 on the EFB. It first filled both wings to full (2,176 gal, 14,579 lb) while draining the center.
  - Payload (START BOARDING, about 25 minutes, cargo-door notifications; it zeroes the stations first): passengers
    **30,800 lb = 176 seats × 175 lb** (the plan's 224 capped at the cabin's 16 + 160 seats), split by BlueBird
    2,800 First / 28,000 Economy (shown as 15/16 and 151/160 passengers, about 185 lb each); cargo **20,659 lb =
    payload − passenger weight**, split evenly between the holds but capped at the forward hold's 10,300 lb: forward
    10,300, aft 10,359. Result: ZFW 182,734, ZFW CG 28.9 %, gross 208,239, CG 31.2 %.
  - The EFB was left on Home, the mirror removed, the camera restored (cockpit view 1/1).
- **EFB Settings > Aircraft (session 6, 2026-10-25 afternoon)**: Settings icon (1073,190), then the AIRCRAFT menu
  item (98,244). The page lists, top to bottom, at EFB y about 128, 192, 249, 310, 371, 434, 492, 553, 614, 674, 735,
  793, 853, 914, 975: ADAPTIVE COCKPIT LIGHTING (AUTOMATIC), TOD PAUSE DISTANCE (NM), NOSEWHEEL STEERING, DISPLAYS
  BOOTUP SPEED (REALISTIC), SYNC BAROMETERS (OFF), INITIAL COCKPIT DOOR (OPEN), PARKING BRAKE MODE (REALISTIC), IRS
  ALIGNMENT TIME (slider, FULL), BOARDING & LOAD RATE (REALISTIC), AUTOMATIC BOARDING (OFF), AUTOMATIC GPU/JETWAY
  (ON), HOPPIE ACARS (OFF), CPDLC NETWORK (HOPPIE), INITIAL CHOCKS IN PLACE (ON), DIRTY PANEL STATES (OFF); the switches
  sit at EFB x about 1154. **NOSEWHEEL STEERING switched from REALISTIC (tiller) to RUDDER** with one click at
  (1154,249), at the owner's request (no one uses a tiller): `efb_settings.json` `tiller_realistic` read `false`
  right after (global setting). The EFB was left on Home.
- **EFB Ground Services (session 6)**: Home, Aircraft (320,190), GROUND SERVICES menu item (128,368). Tiles: JETWAY,
  GPU, FWD STAIR, CARGO SVC, LEFT/RIGHT AIR START, GROUND A/C, CHOCKS, FUEL TRUCK, AFT STAIRS, CATERING (red = removed
  or off), GND SRVC Mode BBS, REQ PB TUG, REMOVE TUG and three direction arrows. **The CHOCKS tile (404,556) toggles
  `L:BBS757_vis_chocks`** (0 removed, 1 in place): measured 0 → 1 → 0 with two taps; left removed. The owner's
  INITIAL CHOCKS IN PLACE is ON, so chocks are attached at each load at a stop.
- **Stock events on the 757 (session 6, on the ground, engines off, APU on; each restored after)**: BlueBird's own
  variables are the truth (stock `BRAKE PARKING POSITION` read 0 with BlueBird's brake set; stock throttle 1 read
  100%). `K:FLAPS_INCR` moved stock `FLAPS HANDLE INDEX` to 1 and BlueBird's `L:B757_FC_FLAP_LEVER` 0 → 17 (surfaces
  stayed up, no hydraulics); `K:FLAPS_UP` returned both to 0. `K:SPOILERS_ARM_TOGGLE` set stock `SPOILERS ARMED` and
  `L:B757_FC_SPOILER_LEVER` 10 (ARMED); toggled back to 0. `K:PARKING_BRAKES` and one `K:BRAKES` left
  `L:B757_FC_PARK_BRAKE` at 1 (PARKING BRAKE MODE is REALISTIC: release is by the pedals, to measure with held
  brakes). `K:KOHLSMAN_SET` 16320 set ALL THREE altimeters (`KOHLSMAN SETTING MB:1..3`) to 1020 (reads lag a few
  seconds); `K:KOHLSMAN_INC` moves only altimeter 1 by 0.01 inHg. `K:XPNDR_SET` takes BCD (0x4743 → 4743; a decimal
  4743 gave 1207). `K:COM_STBY_RADIO_SET_HZ` 124650000 set stock COM 1 standby (whether BlueBird's tuning head shows
  it is unmeasured). DH is the stock `DECISION HEIGHT` (the ADI showed DH200 with stock 200), not settable; one
  `K:INCREASE_DECISION_HEIGHT` gave 210 and one `DECREASE` 200; `L:B757_ADI_1_DH_REF` stayed 0 (a knob animation
  counter). Not tested on the ground: gear up, thrust, stab trim (no hydraulics). Seen: `L:BBS757_THRUST_LEVER_L`
  100.006 and `_R` 0 with the engines off (a hardware axis or a leftover; to check before engine start).
- **Calculator string length (session 4)**: the simconnect MCP refuses calculator strings longer than about 220
  characters ("negative count": it pads to a 256-byte message), so a 25-event speed burst could not be measured through
  it. MSFSBA's own MobiFlight channel is 1,024 bytes (`MobiFlightWasmModule.MAX_COMMAND_SIZE`).

## After the first flight (session 7, 2026-10-25 evening, at KATL gate A2, APU on)

The first flight (debug.log kept in the session scratchpad) ended off the end of KATL 09L: 84 s from brake release to
140 kt, liftoff at the runway end. Measured afterwards through the simconnect MCP's calculator path (unique prefixes;
identical consecutive commands are dropped by MobiFlight, [VAR-4]) and window captures of the PFD, MCP and ND:

- **MCP speed knob accelerates within a burst**: `K:AP_SPD_VAR_INC/DEC` events that arrive together move the target
  1 kt each for events 1-10, 2 kt for 11-25 and 5 kt from the 26th. Measured: 10 events +10, 20 +30, 25 +40, 30 +65,
  50 hit the 100 kt floor; the flight's bursts fit exactly (53 events +180, 47 hit the floor). A burst is one
  calculator call or several back to back; a pause of about 0.3 s or more starts a new burst (two 10-event calls with
  0.25 s or 0.5 s between them moved 20). An RPN loop fires N events from one short string:
  `N sp0 :1 l0 0 > if{ (>K:AP_SPD_VAR_INC) l0 1 - sp0 g1 }`. Restored to 200.
- **MCP buttons: the `_PRESS` pulse works and the `L:B757_MCP_<button>_BUTTON` lights follow.** THR with the A/T
  armed engaged EPR (FMA EPR boxed, THR light 1, levers driven to 100) with the engines off and the F/Ds on or off,
  right after re-arming, and with the engines running (Ready for taxi preset) with the F/Ds on or off; each was
  disconnected at once (`L:B757_AT_DISC_1` pulse) and the levers returned to idle. With an executed route, LNAV armed
  (FMA white LNAV, light 1) with the F/Ds on and also with both off; a second press disarmed it. The flight's THR
  press at the hold short (engines running, F/Ds off, A/T presumably armed) did not engage: not reproduced, cause
  open.
- **A/T and F/D at load**: every BlueBird panel state, cold and dark included, has `BBS_AUTO_THROTTLE` 1 and
  `AT_ARM_SWITCH` 0 (ON: the XML tooltip says 0 "Auto-throttle ON"); `BBS_FD1/2_SWITCH` 0 in cold and dark and
  quickturn, 1 in every other state.
- **EXEC light**: `L:FMC_EXEC_ACTIVE_1` / `_2` went 1 on both CDUs after ACTIVATE (route loaded by ACARS INIT FLT and
  RTE SEND/LOAD through the CDU key L:vars) and 0 after EXEC.
- **Thrust levers**: BlueBird ignored `K:THROTTLE_CUT`, `K:THROTTLE_SET` 0, `K:AXIS_THROTTLE_SET`,
  `K:THROTTLE_AXIS_SET_EX1` and `K:THROTTLE_INCR` (calculator and SimConnect) with the A/T off; the owner's hardware
  throttle moved both levers together; a direct write of `L:BBS757_THRUST_LEVER_L/R` (what the cockpit's drag code
  writes, percent) moved them and the stock throttle positions followed.
- **Stab trim**: the indicator strip shows units = -0.002148 d² + 0.803152 d + 1.11308 (at least 0.436), d =
  `A:ELEVATOR TRIM POSITION` in degrees, while circuit 286 is powered (cockpit XML). At load: 2.3° = 2.95 units. Unlike
  thrust, the stock trim events reach it: five SimConnect `ELEV_TRIM_UP` moved 2.300° to 2.381° (about 0.016° each,
  electric hydraulic pumps on, engines off) and five `ELEV_TRIM_DN` back to 2.300°. During the first flight the
  shared `MON_ElevatorTrim` row never changed, so the takeoff used the loaded 2.95 units.
- **EFB Aircraft app keeps its last page**: Home, then Aircraft reopened on GROUND SERVICES after a chocks tap, not on
  COCKPIT PRESETS. The left menu's COCKPIT PRESETS item is at about (127,178); BA's preset and quick-start sequences
  must tap it first. Measured preset positions (mirror on the ND): Ready for taxi (433,369), Ready for takeoff
  (433,421), Engine quick start (822,524), IRS rapid align (about 1209,519), CABIN & DOORS about (119,427). Engine quick
  start did nothing from the APU-on gate state. Ready for taxi left the parking brake set and the chocks as they were.
- **EFB sliders and AUTOMATIC BOARDING (same session)**: the EFB canvas forwards pointer input as Coherent calls
  (`WASM_MOUSE_MOVE`/`DOWN`/`UP`/`CLICK`/`WHEEL`/`LEAVE`; the JS handlers track buttons 1 and 2 only). Taps work on
  buttons, tiles, menu items and the steering switch, and a hover shows tooltips (BOARDING & LOAD RATE: "Real-world
  style loading/boarding timing"). But neither slider moved (BOARDING & LOAD RATE knob at about (905,614) on
  Settings > Aircraft, IRS ALIGNMENT TIME at the right end near (1192,553)): not with a tap on the track, a drag with
  button 0 or 1 spread over 1-2 s of timers, or ten wheel notches either way. The AUTOMATIC BOARDING switch (row at
  about y 677, switch near x 1150-1170) ignored three taps and a tap on its label, while the steering switch takes one
  tap: why is open (a state it is disabled in, perhaps). BlueBird's settings keys for them: `loading_speed` (0 at
  REALISTIC) and `automatic_loading` (false); neither changed. Settings > GENERAL holds LOCK KEYBOARD TO EFB, EFB
  POWERED ON DURING INITIALIZATION, EFB BRIGHTNESS MODE, EFB BACKGROUND, LIVE AUTOBRAKE INFO, NOTIFICATIONS FADE-OUT
  TIMER, EFB COLORS THEME. Left on Home, mirror removed.
- **BlueBird's own loading through its EFB (Task 11e step 0, same evening, at the gate, APU on)**. Start: fuel 960.5 /
  996 / 0 gal (13,109 lb); stations First 2,963, Economy 28,334, FWD 0, AFT 0 lb (left over from the flight).
  - Home, Aircraft, FUEL (67,237), IMPORT FROM SIMBRIEF (508,962), with no FETCH SIMBRIEF OFP on the ACARS dashboard
    before it (the session had an ACARS INIT FLT on the CDU): fueling started at once, overlay "FUELING IN PROGRESS…"
    with a falling count and the notification "FUELING INITIATED"; no question (fuel below the plan). The wings
    equalized first, then rose together: 1,219.9 gal per wing, center 0, 16,346 lb (BlueBird's 6.72 lb/gal makes it
    the plan's 16,400) about 2 minutes after the tap.
  - START BOARDING (1231,24) about a minute after IMPORT, during fueling: no question; boarding began at once, the
    stations zeroed and then filled; BlueBird's notification panel opened at the right (FUEL / CARGO / PAX progress,
    cargo doors opened). Done within about 40 minutes (checked then): First 1,850 (10/16), Economy 19,240 (104/160) =
    21,090 lb, the plan's; cargo 3,990 lb all FORWARD, aft 0. BlueBird fills the forward hold first up to its
    10,300 lb, then aft (the morning's 20,659 lb came out 10,300/10,359 the same way), not half per hold.
  - Completion: the Fuel page shows the in-page box "FUEL ADJUSTMENT IS COMPLETE!" (OK about (822,692)); boarding
    completion was a notification only ("BOARDING IS COMPLETE", the doors closing); no box on WEIGHT & BALANCE, which
    read EMPTY 130,870, ZFW 156,280, ZFW CG 22.4 %, BLK FUEL 16,250, TAKE-OFF 172,065, LANDING 164,322, gross 172,605,
    CG 24.8 %. Neither box blocks IMPORT. The aircraft was left loaded, EFB on Home (COCKPIT PRESETS last), mirror
    removed.
  - Read again from the same session's FUEL page captures (scratchpad e1-e3, taken 19:50-20:31 PC time) for the final
    review of Task 11: (400,648), the defuel question's YES, is plain page background on the FUEL page with no
    question up, left of the "FUELING IN PROGRESS…" overlay and the "FUEL ADJUSTMENT IS COMPLETE!" box (both span
    about x 530-1117, y 581-720). No tap was made there without a question up. START BOARDING (1231,24) is a top-bar
    button whose face follows BlueBird's state: green "START BOARDING" before boarding (e1, during fueling), a darker
    "BOARDING …" status button during it (e2), and no button at all after "BOARDING IS COMPLETE" (e3). What a press
    does while it reads "BOARDING …" is unmeasured; after completion a press lands on empty bar.

## Gap investigation (session 8, 2026-10-25 night, KATL gate A1, APU on, FMC programmed KATL-KMIA)

For `plans/02-flight-gaps.md`. Title "BlueBird 757-200 PW NO CABIN", engines off, GPU connected but not in use, MSFSBA
not running. Reads through the Coherent debugger (`SimVar.GetSimVarValue` in the `BBSTablet` view, all 2,173 WASM and
XML L:var names in one call, diffed before and after each action against a noise list of the vars that drift on their
own); writes through the MobiFlight calculator (simconnect MCP). Every change was put back; the MCP's own L:var reads
went stale once (TCAS knob), so results were confirmed through the debugger. Pictures: window captures, the EFB through
the ND mirror (in this session's VCockpit08 the captain's ND is the gauge at page (21,735) 704 × 859, the ADI at
(21,8)).

- **COM radios use the stock COM variables.** One `L:B757_VHF_L_FRAC_INC` +1 moved `COM STANDBY FREQUENCY:1` 120.900 →
  120.905 (a 5 kHz step: 8.33 kHz channels); the WASM clears the request to 0. `B757_VHF_C_*` tunes COM 2 and
  `B757_VHF_R_*` COM 3 (radio tuning panels: `BBS_RTP_L/C/R_LIGHTS` read 2, 4, 8 = VHF L, C, R selected). Holding
  `L:B757_PED_COMHEAD_1_SWAP_BUTTON` 1 then 0 swapped COM 1 active and standby. `K:COM2_STBY_RADIO_SET_HZ` 118275000 set
  COM 2 standby, it stayed, and a VHF C knob step then continued from it (118.280): the WASM keeps no frequency of its
  own. Start values: COM 1 121.900 / 120.900, COM 2 121.500 / 130.800, COM 3 124.850 / 124.850.
- **VOR and ILS are BlueBird's own receivers, not the stock NAV radios.** The VOR/DME panel showed 116.90 AUTO (window
  capture) while `NAV ACTIVE FREQUENCY:1..3` all read 110.50 and `NAV OBS:1..3` 0, unchanged by everything below.
  - `L:B757_MCP_VOR1_MODE_AUTO` 1; the VOR/DME 1 button (`L:B757_MCP_VORDME_1_BUTTON` 1 then 0) toggles it (0 =
    MAN). In AUTO, `L:B757_VOR_L_FREQ_WHOLE_INC` +1 is consumed and changes nothing; in MAN the panel showed 108.40 MAN
    after one whole step (from a preset 107.40). Back to AUTO with the same button.
  - The CDU NAV RAD page (captain's) reads `VOR L` / `VOR R` with `116.90AGSU` (A = auto, ident GSU), `CRS`, `RADIAL`,
    and a `PRESELECT` row; no ILS or ADF line. Typed `113.90` + LSK 1L tuned VOR L (`113.90R`, R = remote manual,
    CRS 000); `DELETE` + 1L returned it to `116.90AGSU`. A CDU message ("INVALID ENTRY", "VOR 113.90 INVALID") covers
    the scratchpad, and a CLR clears only the message: two earlier entries had piled up behind it. VOR R showed
    `------` (unpowered or no station) and refused an entry.
  - `L:B757_ILS_FREQ_WHOLE_INC` +1 was consumed and left every stock NAV variable alone; the ILS panel (aft pedestal)
    is in no instrument view, so its LCD was not seen. Writing `L:BBS_ILS_COURSE` 95 held and left `NAV OBS:1..3` at 0.
    No variable carries a tuned VOR or ILS frequency (BlueBird's published list agrees: course read/write only).
  - APPROACH REF (first officer's CDU) shows the FMC's runway ILS: `ILS 09L 110.50 IHZK`, `FRONT CRS 095°`.
- **Landing altitude**: `L:B757_OVHD_LDG_ELEV` 1000 adopts a direct write (1030 held), but the panel's drums
  (`L:B757_OVHD_LDG_ALT_2000`, `_200`, `_20`: the thousands, hundreds and tens digits, read 1/0/0 at 1000) only
  refresh on a knob step. One
  `L:B757_OVHD_LDG_ELEV_CHG` +1 = +10 ft (1030 → 1040, drums 1/0/4). Exact set: write the target − 10, then one CHG +1
  (990 + step → 1000, drums 1/0/0). No FLT ALT selector exists.
- **Emergency lights**: `L:B757_OVHD_PASS_OXY_Toggle_Switch_CLICK` 1 (ARMED) with `L:B757_OVHD_SWITCH_GAURD` 0
  (closed); not changed this session. The map's "Passenger Oxygen Switch Guard" is this guard.
- **MCP**: on the ground, a `B757_MCP_SPD_BUTTON_PRESS` pulse changed no variable at all (light stayed 0, levers idle,
  A/T armed). An IAS/MACH changeover pulse (`L:B757_MCP_AP_SPD_MACH_SW` 1, then 0 1.5 s later) changed nothing either
  (session 4 saw the same). `L:BBS_TARGET_MACH` reads 0 in IAS mode. The speed window drums read
  `B757_MCP_Gau_SPD_AUTO_100/10/1` = 20/10/10 at 200 kt (heading 000 = 0/10/10): a digit d reads 10·d, 0 reads 10.
  `L:BBS_VNAV_TOD` and `BBS_VNAV_TOD_PROGRESS` read 429.24 with the route 568 NM long (distance to top of descent, a
  guess to confirm in flight).
- **EFB GPU tile** (Ground Services, through the ND mirror: "GPU CONNECTED", "NOT IN USE"; CHOCKS green): one tap at
  EFB (1005,231) toggled it off: `BBS_757_GPU_AC_PWR_CONNECTED`, `_CART_VISIBLE`, `_PWR_NOT_IN_USE` 1 → 0, `_DOOR`
  100 → 0, `B757_OVHD_APUGENEXTPWR_R_AVAIL_DECAL` (EXT PWR AVAIL) 1 → 0. A second tap reconnected it within 4 s. The
  position was taken from the mirror's scale, then proven by the taps. The owner's AUTOMATIC GPU/JETWAY is ON.
- **Thrust levers take a direct write**: `L:BBS757_THRUST_LEVER_L` 20 held and drove `GENERAL ENG THROTTLE LEVER
  POSITION:1` to 15.67 %; written back to 0.
- **VREF by weight** (first officer's APPROACH REF, a trial GROSS WT typed into 1L, then `DELETE` + 1L back to the
  FMC's 178.6): 140.0 → VREF 20/25/30 119/111/109; 150 124/115/113; 160 128/119/117; 170 132/123/121; 178.6 (the
  FMC's) 135/126/124; 180 136/127/125; 190 140/131/129; 200 144/135/133; 210 148/139/137; 220 151/142/140; 230
  155/146/144; 240 158/150/148; 250 162/154/152. 120 and 130 and 255 are INVALID ENTRY (range 140-250). TAKEOFF REF
  read flaps 5, V1/VR/V2 138/138/140, trim 4.00. The cockpit placard: MAX FLAP EXT SPEEDS 1 240, 5 220, 15 210, 20 195,
  25 190, 30 162 kt (window capture).
- **PROG page 1** (first officer's CDU): `TO (INTC) 1 0210Z 13.4`, `NEXT GRITZ 6 0211Z 12.8`, `DEST KMIA 568 0332Z
  6.1`, `ECON SPD 299/.785`, `TO T/C 0218Z/ 56NM`. Page 2: winds, XTK/VTK error, TAS, SAT, fuel used, totalizer 13.7
  and calculated 13.8.
- **Control-map samples** (the map's own click codes): Logo Lights toggled `L:B757_OVHD_LOGO_BUTTON` and the stock
  `LIGHT LOGO` off and on; one TCAS Mode cw step went 0 → 1 (stock `TRANSPONDER STATE:1` 5) and ccw back (1).
- **Left as found**: frequencies, VOR AUTO, landing altitude 1000, GPU connected, logo on, TCAS STBY, levers 0, ILS
  course 0, camera cockpit view 1, EFB Home, both CDUs on their pages. The captain's scratchpad message "TAKEOFF DATA
  UPLINK" was cleared by a CLR.

## WASM-only variables, first reads (session 2, APU running)

From `wasm-findings.md`'s list; all read through the calculator path:

| Variable | Read | Meaning so far |
|---|---|---|
| `L:B757_APU_STATE` | 1 while spooling, 2 running | enum, EFB labels APU OFF / START / RUN / SHUTDOWN (0-3 presumably) |
| `L:B757_L_BUS_PWR_SRC`, `L:B757_R_BUS_PWR_SRC` | 2, 2 | power source per AC bus; 2 on the APU generator (other values to map) |
| `L:B757_LEFT_PACK_STATE`, `L:B757_RIGHT_PACK_STATE` | 1, 0 | matches the EICAS "R PACK OFF" at that moment |
| `L:BBS_ECAM_Mode` | 1 | lower EICAS page (to map) |
| `L:eK104_MAIN_BAT_RELAY` | 0 with the battery on | expected: relay export is off in the settings |
| `L:B757_DEBUG_PRESSURE_CABIN_ALT_FT` | 0 on the ground | needs a climb |
| `L:BBS_CA_MASTER_CAUTION`, `_WARNING` | 0, 0 | while EICAS showed cautions: probably the lamps after a reset; to measure |
| `L:BBS_EFBX_SET_TOTAL_FUEL_LBS` | wrote 20000, read -1 after 3 s | **the WASM consumes it** (-1 = idle), but fuel did not change in 15 s (19,946 lb, APU burning); probably sets the EFB's fuel target and still needs the EFB's load step or the fuel truck |

## EFB-replacement checks (session 2)

- Navigraph: not signed in (helper `<bbs-tablet>`: `ngAccessToken` null, `ngRefreshToken` empty), but a device
  sign-in request was pending and polling (`ngPoll` true): `nav_device_auth` holds `device_code` (secret, never read
  out), `user_code` (8 characters), `verification_uri` (navigraph.com), `verification_uri_complete`
  (identity.api.navigraph.com), `expires_in` 1800, `interval` 5. Only field names and lengths were read.
- **Navigraph sign-in done by the owner from the read-out code** (user code and the address with the code built in,
  read off `nav_device_auth`): afterwards `ngAccessToken` held 1,102 characters, `ngRefreshToken` 32, `ngPoll` false.
  The pending request had been replaced about every 5 minutes (`nav_auth_time_start` 1,761,558 → 2,082,744 →
  2,152,939, `performance.now()` milliseconds), so a code is good for minutes, not the 30 its `expires_in` says.
- Fuel: stock `FUEL TANK LEFT MAIN QUANTITY` refused a write (stayed 1,476.6 gal). `L:BBS_EFBX_SET_FUEL_LEFT_LBS`
  10000 → left tank 1,488.4 gal within 6 s, setter reads -1 after. `L:BBS_EFBX_SET_TOTAL_FUEL_LBS` 20000 → consumed,
  no change in 15 s.

## State left in the sim after session 2

Battery ON, APU running with its generator on the AC buses, CDUs on (captain on POS INIT 1/4, first officer on IDENT,
scratchpads empty), MCP heading 000, left main tank set to 10,000 lb (about 80 lb more than loaded). Everything else
as loaded.

## Camera and displays

- Instrument camera views (`cameras.cfg`, category Instrument): ADI/EHSI, EICAS, Overhead, IRS, Engines, FMC,
  Circuit Breakers. **Measured: instrument index 2 is Overhead** (`CAMERA VIEW TYPE AND INDEX:0` = 2, `:1` = 2,
  captured). **Instrument index 1 is the EICAS view**, and it frames far more than EICAS: the whole MCP, both EICAS
  CRTs (upper messages legible at 1600 px wide: "R FUEL SYS PRESS", "R ENG SHUTDOWN", "R PACK OFF", "PAGE 1", TAT,
  EPR), the standby instruments, autobrake, flap gauge, gear lever and the FO-side airspeed: a strong candidate frame
  for AI display reads. Cockpit pilot view: `:0` = 1, `:1` = 0. When switching type and index, write them in
  separate calls and re-read: one `:0` write was reported as not taking effect and had to be repeated. `CAMERA GAMEPLAY PITCH YAW` is not settable (no way found yet to
  look left at the EFB). `K:VIEW_LEFT` did nothing.
- The WASM displays (PFD, ND, EICAS, CDU, EFB, radio LCDs) render natively: their Coherent views (VCockpit29/30) hold
  no DOM canvas, so their pixels cannot be read through the debugger. Window captures are the only way to see them.

## Coherent views with the 757 loaded

`VCockpit28 - BBSTablet - WasmInstrument - WasmInstrument` (the hidden `bbs-tablet.html` helper and the systems
gauges), `VCockpit29` (eleven instruments including `BBSNavigraphMap`), `VCockpit30` (eighteen WASM instruments).
A read-only Comm Bus listener injected into VCockpit28 registers fine (`RegisterViewListener('JS_LISTENER_COMM_BUS')`
then `.on('BBS_CDU_DISPLAY_1' | '_2')`); with the aircraft unpowered it received nothing, as expected for a sender
that only sends on change.

## Local servers

No new listening port appeared with the 757 loaded (compared against the Fenix-loaded baseline): BlueBird runs no
local server. Nothing listens on 8320 and the sim made no connection to it during the checks.
