# Plan 12 measurements: spring trims, manual cabin altitude, brightness, zone temperatures, auto rate, DDRMI

2026-10-28, on the ground, BlueBird 757-200 cold and dark with ground power connected (EXT PWR on, both AC buses on
external power) and the battery on, BBS Nest running, MSFSBA closed; the owner asked for plan 12 to be worked with the
simconnect MCP (first) and the Coherent debugger (fallback). Run by Claude. Writes through the MCP's calculator path
(MobiFlight, as the app writes), reads through the MCP and through the L:var snapshot tool
(`docs/b757/tools/live/snap.sh`, 2,173 names, diffed with `diff_snaps.py` and `noise.txt`). BlueBird's published list
(support.bluebirdsim.com/docs/simvars, read 2026-10-28) gave the value meanings checked here.

## A trap of the MCP, and what it did to the aircraft

The MCP's calculator tool in `mode: "read"` registers the whole expression with MobiFlight as a POLLED variable. A
read-mode expression that writes (`2 (>L:B757_PEDESTAL_RUDDER_TRIM_KNOB) (E:SIMULATION TIME, second)`, used to time a
hold) is re-run on every poll, and several such expressions interleave. That pressed the spring switches over and
over (the rudder trim ran to −17 and later to +16.7 units), flickered the C1 and C2 hydraulic pumps (a toggle press in
read mode) and made a later write "not take". `msfs_disconnect` did not stop it; `msfs_connect` again did, leaving
the aileron trim switch on 0 (trimming left) until it was released. Writes belong in `mode: "execute"` only. The app
is not affected (its writes are one-shot `ExecuteCalculatorCodeUnique` calls), but it is the measured case for the
spring buttons' verified release (below).

Put back afterwards (a hold-and-release script in the Coherent debugger, which can time a release; the MCP round
trip cannot): rudder trim +0.04 units (was 0), aileron trim yoke 1.489 (was 1.500), stabilizer trim 2.413° (was
2.300°: each shortest blip moves about 0.29°), C1 and C2 pumps off, Mode Select AUTO 1, the manual cabin altitude
switch at 4, every brightness knob 40, the zone knobs 18, Auto Rate 3.5, the DDRMI switches VOR. A final snapshot diff
against the first shows only those trim residuals and their followers (`B757_FC_YOKE`, `BBS_757_trimtab`,
`BBS_RUDDER_PEDAL`, `B757_FC_TILLER_HANDLE`).

## Spring-loaded trims (template `BBS_3POS_TRIM_SWITCH_TEMPLATE`, XML 406-480 and 2857-2903)

The cockpit's switch writes the hold value while the mouse is held and 1 on release (`LEFT_UP_CODE`); nothing in the
XML returns it to 1 on its own. BlueBird's WASM reads the switch every frame and trims while it is off 1
(`RUDDER_TRIM_LEFT/RIGHT`, `AILERON_TRIM_LEFT/RIGHT`, `ELEV_TRIM_DN/UP` in its strings). So a release that is lost
leaves the trim running to its stop: measured twice (rudder to −17 and to +16.7 units).

| Switch | Rest | 0 | 2 | Readout | Rate (measured) |
|---|---|---|---|---|---|
| `L:B757_PEDESTAL_RUDDER_TRIM_KNOB` | 1 | left: `L:B757_PED_RUDDER_TRIM_INDICATOR` falls | right: it rises | `B757_PED_RUDDER_TRIM_INDICATOR`, units −17 to +17 (BlueBird's list; the stop read −17.000) | about 0.85 units/s (+0.54 in 0.64 s; 16.7 units in about 19.5 s); moves without hydraulic pressure |
| `L:BBS_AILERON_TRIM_SWITCH` | 1 | left: `L:B757_FC_YOKE` falls | right: it rises | `B757_FC_YOKE`, the yoke's rotation animation, 0 to 3 with 1.5 neutral (XML 32663-32683: `ANIM_LENGTH` 3, set to 1.5 on load); `BBS757_FC_L/R_AILERON` follow (150 neutral, with hydraulic droop). Stock `AILERON TRIM PCT` stayed 0 throughout | about 0.06 yoke units/s; it reached 0.865 (0.635 left of neutral) and was still moving, so its stop is unmeasured; BlueBird publishes no aileron trim units |
| `L:BBS_ELEV_TRIM_ALTERNATE` | 1 | nose down: `ELEVATOR TRIM POSITION` falls | nose up: it rises | stock `ELEVATOR TRIM POSITION` (the app's existing trim call-out, `Bbs757Readouts.StabTrimUnits`) | about 0.27-0.3°/s (+0.175° in 0.64 s); needs centre hydraulic pressure |

Stock `RUDDER TRIM PCT` stayed 0 while the rudder trim moved: the indicator L:var is the readout. With hydraulics off
(cold and dark) the aileron trim switch moved the yoke animation but the ailerons were not measured moving; the
centre system (C1 and C2 electric pumps on ground power) was used for the aileron and stabilizer runs. The
readbacks lag the release by a few tenths of a second (the stabilizer trim read 2.475° at release and kept moving).

## Manual cabin altitude (`L:B757_OVHD_CABINALT_SWITCH`, Mode Select `L:B757_OVHD_MODE_SELECT_SWITCH` = 2, MAN)

BlueBird's list: 0-3 descend, 4 neutral, 5-8 climb, only in MAN. Measured on the ground with the outflow valve
(`L:BBS_CABIN_VALVE`, 0 closed, 1 fully open) at 1: 0 (descend) for 1.2 s closed it to 0.869; 8 (climb) for 1.3 s
opened it to 0.963, and again to 1 (it stops at 1). Back at 4 the valve held. The cabin altitude
(`L:B757_TEST_CABIN_ALT`, 1,161 ft) does not move on the ground with the valve open, so the valve is the switch's
readable result. Mode Select set back to 0 (AUTO 1).

## Display brightness knobs (`B757_*_SCREEN_KNOB`)

Each knob's L:var is what the brightness system reads, directly: written by the calculator, it moved its output at
once. Every knob read 40 on load (BlueBird's list says 0-30 for the EICAS, HSI and FMC knobs; 40 is their load value
and gives full output, so the range is 0-40 for all eight).

| Knob | Output L:var | 40 | 30 | 20 | 15 | 0 |
|---|---|---|---|---|---|---|
| `B757_PFD_1_SCREEN_KNOB` (captain) | `B757_PFD_BRIGHTNESS_L` | 1.5 | | 0.75 | | 0.1 |
| `B757_HSI_1_SCREEN_KNOB` (captain ND) | `B757_HSI_BRIGHTNESS_L` | 1.5 | 1.125 | | | |
| `B757_EICAS_1_SCREEN_KNOB` (upper EICAS) | `B757_EICAS_BRIGHTNESS` | 1.5 | 1.125 | | | |
| `B757_EICAS_2_SCREEN_KNOB` (lower EICAS) | `B757_EICAS_LOWER_BRIGHTNESS` | 1.5 | | | 0.5625 | |
| `B757_FMC_1_SCREEN_KNOB` (captain CDU) | `B757_CDU_BRIGHTNESS_L` | 1.5 | 1.125 | | | |

Output = knob ÷ 40 × 1.5, with a floor of 0.1 at 0: a knob at 0 dims its display to its minimum, it does not turn it
off. The `_2` knobs follow by name (`B757_PFD_BRIGHTNESS_R`, `B757_HSI_BRIGHTNESS_R`, `B757_CDU_BRIGHTNESS_R` exist
and read 1.5); not written. `BBS757_RASTER_BRIGHTNESS_CA` is not one of the 2,173 names the snapshot reads.

## Zone temperature selectors (`B757_OVHD_<zone>_TEMP_SEL_KNOB_FRAME`)

Zones FLT_DK, FWD_CAB, AFT_CAB (MID_CAB is fixed at 18, BlueBird's list: no consumer). Each frame was written by the
calculator and the zone's readback `B757_OVHD_<zone>_ZONE_TEMP_SEL` (°C) read about a second later; the knob's
`…_KNOB_ANIM_FRAME` followed the frame.

| Frame | 0-2 | 3, 4, 5 | 13 | 18 | 29 | 31, 32, 33 | 34, 35 |
|---|---|---|---|---|---|---|---|
| °C read | 0 (OFF) | 18 | 21.692 | 24 | 29.077 | 30 | 0 (OFF) |

So between frames 5 and 31: °C = 24 + (frame − 18) × 6 / 13 (5 → 18, 18 → 24, 31 → 30), in steps of 0.46 °C; the
frames outside clamp to 18 or 30 or read OFF. Loaded at frame 18 (24 °C).

## Auto Rate (`L:B757_OVHD_AUTORATE_SWITCH`)

Loaded at 3.5 (the index, as plan 03 read from the XML). Written 0 and 13 by the calculator: both held. No other
variable in the snapshot moved on the ground.

## DDRMI switches

The first officer's right switch's own click code, `(L:BBS_DDRMI_RIGHT_FO) ! (>L:BBS_DDRMI_RIGHT_FO) }`, stray brace
included, toggled `BBS_DDRMI_RIGHT_FO` 0 → 1 through the calculator; written back to 0. The four DDRMI rows the map
already holds therefore work as they are.

## The fire test, the P61 test switches and the master lights (later the same day)

The owner asked for the test controls to be tried in the sim. The aircraft was by then IN FLIGHT (about FL353, 272 kt,
both engines running, ground power gone), which was found only after the first write; nothing else was written.

- Engine fire test (`L:B757_PED_FIRE_ENG_BUTTON`, template `BBS757_BLACK_PTT_BUTTON`, read by BlueBird's WASM): held at
  1 for about 3 s through the calculator, it raised `B757_FIRE_BELL` and `BBS_PLAY_FIRE_BELL`, `B757_L/R_ENG_FIRE_ACTIVE`,
  `B757_MAIN_FIRE_1_DECAL`, `B757_PED_LENG/RENG_OVHT_DECAL`, `B757_PED_FAIL_BAR_DECAL`, `B757_L/R_ENG_HANDLE_AUTO_UNLOCK`,
  `B757_L/R_ENG_FIRE_FUEL_CUTOFF_SWITCH` (indications) and both sides' master warning and caution (`BBS_CA_MASTER_*`,
  `BBS_FO_MASTER_*`). Written back to 0, every one of them read 0 again about 2 s later, the master lights included (no
  latch); the fuel control switches (`BBS_fuelcut_PW1/2`) never moved and the engines kept running (N1 about 90 %). So
  the test shows only while the button is held: the app's 200 ms button press gives a blip at most.
The owner then said to continue in flight ("they should not impact flight"). Each test below was held through the
calculator (MCP, execute mode), read with a snapshot diff against an idle one (minus the 22 vars that drift on their
own in flight), released, and read again. Everything read as before afterwards.

- Cargo fire test (`L:B757_PED_FIRE_CARGO_BUTTON`): the whole ENG/APU/CARGO test while held: the engine indications above
  plus `B757_APU_FIRE_ACTIVE`, `B757_APU_HANDLE_AUTO_UNLOCK`, `B757_FWD/AFT_CARGO_FIRE_ACTIVE`, `B757_PED_FIRE_FWD/AFT_DECAL`,
  the bell and both sides' master warning and caution; all cleared on release.
- Wheel well fire test (`L:B757_PED_FIRE_WHLWELL_BUTTON`): the bell, `B757_MAIN_WHEELWELL_1_DECAL`, `B757_MAIN_FIRE_1_DECAL`,
  master warning (both sides, no caution) while held; cleared on release.
- Master warning and caution buttons: during a held fire test, the captain's button (its map press code, then its
  release code) turned the bell off (`B757_FIRE_BELL` 1 → 0) and cleared master warning AND caution on BOTH sides
  (`BBS_CA_MASTER_*`, `BBS_FO_MASTER_*` 1 → 0), the fire lights staying on while the test was held; the first officer's
  button did the same during the cargo test. So they work as the cockpit's (one press cancels both sides and silences
  the bell).
- P61 test switches: WRONG above, corrected by measurement. A single write of a test position is returned to 1 (neutral)
  almost at once: the Asobo 3-state template's `STATE0_TIMER 0.01` returns the switch while no mouse holds it. Read
  about 1 s after one write, the switch was 1 and nothing had happened, so the app's pick (one write) never runs the test.
  Held by writing the position again and again: every 30 ms (about 40 ms in practice, 60 writes in 2.4 s) the switch
  held and the test ran steadily (sampled every 20 ms: shaker on throughout); every 50 ms (about 67 ms in practice) it
  held but the switch read 1 at 2 of 45 samples; every 100 ms (about 130 ms) it dropped out for about 40 ms between
  writes (the stick shaker too). Held that way for 2.4 s each, in flight:
  - stall warning test, left (`BBS757_AIRDATA_STALL_LEFT_P61_TEST_SW` = 2): `BBS757_STICK_SHAKER_CA` 1, the pitch limit
    indicator shown (`BBS757_PLI_VISIBLE` 1, `BBS757_PLI_PITCH_LIMIT_DEG` about 6); right (`…_RIGHT_…` = 2): the first
    officer's shaker `BBS757_STICK_SHAKER_FO` 1 and the indicator; both stopped when the writes stopped;
  - landing configuration test (`BBS757_TO_AND_LAND_CONFIG_P61_TEST_SW` = 2): `B757_MAIN_CONFIG_1_DECAL` 1, master warning
    both sides and its aural (`BBS_PLAY_MASTER_WARNING_A` 1); cleared after (no latch);
  - nothing seen in flight: the takeoff configuration test (that switch = 0), the air data test (stall left = 0), the
    ground proximity test (`BBS757_GRNDPROX_HYDGEN_P61_TEST_SW` = 0; BlueBird's list: implemented at 0) and its
    hydraulic generator test (2), the duct leak / fuel quantity test (0, 2), the wing anti-ice and probe heat tests
    (`BBS757_WINGAI_WINDOWHEAT_P61_TEST_SW` 0, 2), the equipment cooling test (0, 2). The takeoff configuration and ground
    proximity tests are ground tests in the 757 (inferred: they may be inhibited in flight); the others may not be
    modelled. To check on the ground. The yaw damper test was not tried in flight.
- Other test buttons, held about 2 s: the CVR test (`L:B757_OVHD_CVR_Test`) swings the monitor needle
  (`B757_OVHD_CVR_DIAL` 0 → 1) while held, back to 0 on release; the squib test (`L:B757_P6_SQUIB_2_TEST_SWITCH_BUTTON`)
  lights the nine bottle squib lights (`B757_ENG_BTL1/2_L/R_`, `B757_APU_BTL_`, `B757_CARGO_BTL1/2_A/F_SQUIB_LIGHT`) while
  held, off on release; the TCAS test, the takeoff check button (`B757_MAIN_TOCHK_BUTTON`), the captain's oxygen test and
  the ILS test changed nothing but their own var in flight (ground tests, or sound only). The autoland status tests were
  not tried in flight.
- The lamp test (Warning Test, `OVHD_TEST_LTS_PRESS`) is not held: its press toggles `L:OVHD_TEST_LTS` (when circuit 27
  is on) and it stays on after the release (the overhead fault lights, the IRS lights, the radio panels' lights all
  on); a second press turns it off. Turned off again.

## On the ground: the tests that showed nothing in flight, the door, the windows and furniture (the same evening)

2026-10-28, parked at a gate, engines off. The first attempt changed nothing at all, not even the engine fire test:
BBS Nest was not running (spec 1.1: without it no system reacts). With Nest started the owner reloaded the flight cold
and dark; the battery and the APU were switched on (both AC buses on the APU). Each test was held as in flight (the P61
switches re-written every 30 ms for 2.4 s, the buttons written 1 and back to 0 after 3 s, both through the tablet page),
read with a snapshot diff against an idle one (no variable drifted on its own over 12 s), and read again after.

- P61 test switches:
  - takeoff configuration test (`BBS757_TO_AND_LAND_CONFIG_P61_TEST_SW` = 0): `B757_MAIN_CONFIG_1_DECAL` 1, master warning
    both sides and its aural (`BBS_PLAY_MASTER_WARNING_A`); landing configuration (= 2) the same; both cleared after;
  - ground proximity test (`BBS757_GRNDPROX_HYDGEN_P61_TEST_SW` = 0): `B757_MAIN_GNDPROX_1_DECAL` 1 and the glideslope
    aural (`BBS_PLAY_GPWS_GLIDESLOPE`); the GND PROX light stays on for about 15 to 20 s after the test;
  - stall warning left and right (`…_AIRDATA_STALL_LEFT/RIGHT_P61_TEST_SW` = 2): the captain's or first officer's stick
    shaker and the pitch limit indicator (`BBS757_PLI_VISIBLE` 1, about 10°), as in flight;
  - wing anti-ice test (`BBS757_WINGAI_WINDOWHEAT_P61_TEST_SW` = 0): both engine anti-ice VALVE lights
    (`B757_OVHD_ANTI_ICE_ENGL/ENGR_VALVE_DECAL`) on while held; probe heat test (= 2): the seven probe heat lights (captain
    and first officer pitot, L/R AOA, L/R AUX, L TAT) go OFF while held;
  - nothing in any L:var: the hydraulic generator test (`…GRNDPROX_HYDGEN…` = 2), the air data tests (stall left/right
    = 0), the equipment cooling test (0, 2), the yaw damper test (0, 2) and the duct leak / fuel quantity test (0, 2).
    BlueBird's WASM names every one of these switches except the duct leak / fuel quantity one (the final review), so the
    others may show on the EICAS, which no variable gives; the duct leak / fuel quantity switch is read by nothing.
- Held buttons: the autoland status tests 1 and 2, both sides (`B757_MAIN_L/R_AUTOLAND_1/2_BUTTON`), change the autoland
  status lights while held (`…_AUTOLAND_1_BUTTON_ON` 1 → 0 or 2, `…_2_BUTTON_ON` 2 → 0) and they come back after the
  release. The takeoff config check (`B757_MAIN_TOCHK_BUTTON`, 1 s) with the flaps up: the CONFIG light, master warning
  both sides and both aurals, and they STAY after the release. The captain's master warning and caution button (its map
  press and release) then cleared the master warning on both sides and the CONFIG light. The four oxygen tests, the TCAS
  test, the ILS test and the weather radar test changed no L:var on the ground (sound or display only, or not modelled).
- The radio panel test: its release writes only an `O:` var, and `L:B757_PED_NAV_1_TEST_BUTTON` stays 1 after it (read 3 s
  later). The cockpit's own click does the same (the XML's release is that `O:` write), and no gauge or WASM string names
  the L:var, so it changes nothing; it was put back to 0 by hand.
- The cockpit door (`B757_COCKPIT_DOOR`, the map's press): open → closed, `B757_DECAL_LOCK_FAIL` off and
  `BBS757_FDAS_DISARM` 0; closed → open, the lock fail light on again (on the ground with circuit 312 powered). A second
  identical calculator string through the MCP did nothing: MobiFlight skips a string the same as the last one, which is
  why the app prefixes each write (`<n> 0 *`).

### Windows and furniture (for Task 11)

Each variable written on the tablet page, read 2.5 s later, written back, read again: every one held its value and went
back. Side windows `B757_L/R_COCKPIT_WINDOW` 0 → 1 (open; `L:AVERAGE_WINDOW` follows), and an aft window shade written 1
while its window was open read 0 (the XML puts `L3/R3_SHADE_GUIDE` up while the window is open, and its click moves it
only while the window is closed). The four shades `L2/L3/R2/R3_SHADE_GUIDE` 0 → 1; the sun visors `B757_SUNVISOR_L/R_vis`
0 → 1 and `B757_SUNVISOR_L/R` 0 → 18; the armrests (captain, first officer, both jumpseats, `…_ARM_REST_1/2`) 0 → 1; the
headrests `B757_CAPTAIN/FO_SEAT_HEAD_REST_1` 0 → 18; the jumpseats `B757_JUMP_1/2_SEAT_BASE`, the chart holders
`B757_CAPTAIN/FO_MAIN_CHART` and the floor box 0 → 1; the stowage doors `B757_STOWAGE_DOOR_1/2/3` 0 → 17/18/18; the seats
`B757_CAPT_SEAT` / `B757_FO_SEAT` 100 → 0. Seen only as numbers: which way each part moves is BlueBird's code or the
model's, not seen here.

### The tablet's ground air and air start tiles (for plan 11)

On the tablet's GROUND SERVICES page (Home, Aircraft, GROUND SERVICES; proved by the AFT STAIRS tile, 0 → 1 → 0), taps
through the app's EFB agent, every variable read after each: GROUND A/C (411, 373) toggles `BBS757_vis_aircon` 0 → 1 → 0;
LEFT AIR START (619, 436) and RIGHT AIR START (1009, 436 or 1009, 455) both toggle the same `BBS757_vis_airstart` with
`BBS757_air_start_door` (0 → 1 → 0); (1034, 434) hit nothing. On plan 11's runway session these tiles were grey: at a gate
they work. `B757_ENG_1/2_AIR_STARTER_RATIO` stayed 0 with the cart connected and the engines off. Everything was removed
again and the tablet left on Home.
