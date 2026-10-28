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
- P61 test switches (template `BBS_3POS_P61_TEST_SWITCH`, XML 490): `MOMENTARY_SWITCH` with `LEFT_UP_CODE` writing 1, so
  only the cockpit mouse's release returns a switch to neutral; an outside write of a test position stays there (read
  from the source, not tried: not in flight).
- Master warning and caution buttons (`B757_MCP_btn_master_pilot` / `_copilot`): not measured; the fire test's lights
  went out on release by themselves, so there was nothing to cancel. Needs a caution that stays on, on the ground.
