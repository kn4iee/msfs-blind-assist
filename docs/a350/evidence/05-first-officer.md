# Plan 05: the First Officer and its automation on 1.2.8

Measured 2026-11-02 on the iniBuilds A350 1.2.8 at VCBI on one flight from stand A6, cold and dark: every First Officer flow up to the line-up, a
scripted take-off from runway 04 (`a350_takeoff8.py`: the rudder on the centreline, the rotation, AP 1 pressed from 5 ft, the stock `GEAR_UP` at
50 ft), a level segment at 5,000 ft, a managed approach to the ILS Z 04 with a go-around from about 2,000 ft, a second approach on selected
speeds, a landing, then the After Landing, Parking and Securing flows. Tools: `A350Probe repl` with `A350_LOG=1` and its First Officer driver
(`fo run|log|items`, automation on), a once-a-second log of IAS, height, V/S, the flap lever, green dot, S, F, VFE next, the gear lever, the
thrust lever angle, the FMS phase (`INI_flight_phase`) and AP 1, and `debug.log`'s sent lines for the time of each command.

## Task 1: the flap margin, the landing gate and the go-around

Every flap command below is the First Officer's own (`AIRLINER_Flaps_Set` in `debug.log`), with the speed of the nearest log line.

| Time | Command | IAS | Target | VFE next | Where |
| --- | --- | --- | --- | --- | --- |
| 12:03:36 | flaps 1 | 171-175 | green dot 172 | 255 | first approach, the descent from 5,000 ft starting |
| 12:06:01 | flaps 0 | about 200 | — | 212 | 18 s after TOGA, at CL |
| 12:13:23 | flaps 1 | 174 | green dot 172 | 255 | second approach, level at 2,980 ft |
| 12:13:44 | flaps 2 | 154 | S 152 (S+2 selected) | 212 | level at 3,000 ft |
| 12:15:19 | flaps 3 | 141 | F 138 (F+2 selected) | 195 | level at 2,965 ft |
| 12:21:04 | flaps FULL | 142 | the landing gate | 186 | 1,500 ft AGL, on the glideslope |

- **Step 1, the miss:** the approach phase was activated level at 5,000 ft (phase 6 from 11:59:59) with managed speed. The speed came down to
  green dot by about 12:01:10 and held 170-174 kt, inside `ExtendWithinKts`, for over two minutes with no flaps 1. Flaps 1 came only
  at 12:03:36, once the descent reached 300 ft below the leg's highest altitude: the approach context armed only on that descent, and this
  approach had none before green dot. Fixed test-first (`2d3b12274`, `A350FlapMarginTests`): the FMS's own approach phase also arms it on a
  level segment, never while climbing (a go-around reads phase 6 for a moment before the climb is seen) and never on an unread phase. The
  second approach extended flaps 1 level only because the first one's descent had already armed it.
- **Step 1, the margins:** each extension within 3 kt of its target (flaps 1 at 174 against 172; flaps 2 at 154 against 152; flaps 3 at 141
  against 138), none above VFE next, FULL commanded at 1,500 ft AGL and read on the lever at about 1,370 ft (before the 1,300 ft gate).
- **Step 2:** on selected speeds 2 kt above S and then above F (12:12:38 154 kt, 12:15:00 141 kt) the steps still came, flaps 2 and 3 above.
- **Step 3:** TOGA at 12:05:43 at about 1,960 ft AGL with flaps 1, CL at 12:06:01. The FMS phase went from 6 to 3 (climb), not 9. No extension
  in the go-around; the First Officer retracted to 0 at 12:06:01, 18 s after TOGA (past `GoAroundLeverHoldSeconds`), at about 200 kt, below
  VFE next 212 (the speed reached 234 kt by 12:06:10 with the lever already at 0). The approach was then activated again and flown (Step 2).
- **Step 4 (the flight step again), 14:03-14:24 on a fresh flight from the menu:** a scripted take-off from runway 04, a left-hand circuit at
  5,000 ft, the approach phase activated level on the downwind (phase 6 at 14:09:40) with managed speed (the push took this time), the ILS Z
  04 captured from below at 7.3 NM, an autoland with ROLL OUT. Every step came level and within 3 kt of its target:

  | Time | Command | IAS | Target | Where |
  | --- | --- | --- | --- | --- |
  | 14:11:29 | flaps 1 | 172 | green dot 172 | level at 4,986 ft, phase 6 |
  | 14:12:43 | flaps 2 | 152 | S 153 | level at 4,984 ft |
  | 14:13:15 | flaps 3 | 139 | F 138-140 | level at 4,991 ft |
  | 14:18:13 | flaps FULL | 134 | F 132 (conf 3), gear down 1.3 s before | descending through 2,440 ft on the glideslope |

- **A second miss, found by that flight:** flaps 1 waited 40 s at green dot (the speed reached 174 kt at 14:10:48). Extension stopped at
  5,000 ft AGL, and level at 5,000 ft over ground near sea level the sim's PLANE ALT ABOVE GROUND (what the First Officer reads, about 15 ft
  more than the gear's height) read 4,999 to 5,008 ft: flaps 1 came once the aircraft had drifted 15 ft low. Fixed test-first (`f9527e830`,
  `A_level_approach_at_5000_ft_over_a_sea_level_field_extends_flaps_1`, `An_approach_phase_far_above_the_field_extends_nothing`): in the
  FMS's approach phase the ceiling is 10,000 ft AGL, armed by a descent alone it stays 5,000 ft. Its own flight step is still to fly: the
  second circuit (14:36, after a ground placement back to the line-up point) took off with no SRS, the FMS stayed in phase 1 (the speed
  target read 100 kt before the roll: the FMS after a landing, re-initialised on INIT, FUEL&LOAD and PERF, did not guide the take-off), so
  ACTIVATE APPR was never offered; the aircraft was put back on the runway.

## Task 2: the gear the automation raises

- The shared automation sends the stock `GEAR_UP` (`UniversalAutomationService`, "Positive rate. Gear up."). The same event, sent by the
  take-off script at 57 ft AGL (11:52:20), was taken: `INI_GEAR_HANDLE_STATUS` read 0 2.5 s later. No A350 hook is needed (Step 3: no handoff).
- Again on the fresh flight at 14:37:12 (63 ft): the handle read 0 3 s later. The After Takeoff flow's `AT_GEAR_UP_CHECK` read UP in flight
  (14:05:57, "already set").
- Not measured: the automation itself sending it (the probe runs the First Officer, not the shared service) and the stock `GEAR_DOWN` on an
  approach (the gear came down by the landing script's row).

## Task 3: the lowest autopilot engage height (T3)

- AP 1 was pressed once at 5 ft AGL, 1.3 s after lift-off, and `INI_ap1_on` read 1 at 18 ft: the A350 accepts the autopilot from lift-off. Its
  `MinimumAutopilotEngageAltitudeAgl` (0) is right; the user's engage height (default 350 ft) governs. No change (Step 2 not needed).

## Task 4: the normal checklists

| Group | Flow | Unfinished after the flow | Captain lines |
| --- | --- | --- | --- |
| COCKPIT_PREP | completed | none | — |
| BEFORE_START | completed 11:42:12 | none | cleared altitude, ground power and chocks (OIS GROUND), surveillance (MFD SURV), PERF page, trim, doors (OIS GROUND), clearance |
| ENGINE_START | completed 11:46:13 | none | — |
| AFTER_START | completed 11:46:46 | none | anti-ice, flaps for take-off |
| TAXI | completed 11:48:32 | none | flight controls, predictive windshear (MFD SURV), flaps for take-off |
| LINEUP | completed 11:50:29 | none | TCAS TA/RA (MFD SURV) |
| AFTER_LANDING | completed 12:25:52 | ALC_FLAPS | flaps up |
| PARKING | completed 12:27:12 | none | transponder and TCAS standby (MFD SURV) |
| SECURE | completed 12:29:45 | none | laptops off |

- BEFORE_START_CL ticked by hand with the beacon off stayed latched (FO-8); AFTER_START_CL and BEFORE_TAKEOFF_CL latched from their flows and
  followed the spoilers' state where FO-8 leaves them unlatched.
- AFTER_LANDING: the flaps are the Captain's; `AL_FLAPS_CHECK` timed out (20 s) and was skipped, so `ALC_FLAPS` stayed open until the lever was
  set to 0 by hand at 12:26:03, as FO-7 says for a skipped step.
- In flight on the fresh flight: AFTER_TAKEOFF completed 14:05:57 with nothing unfinished (gear check read UP); APPROACH completed 14:12:52
  (Captain lines: the landing autobrake, activate the approach phase); LANDING completed 14:18:30 once LOC and G/S had captured (Captain line:
  the missed-approach altitude).
- Its LINEUP after a landing and a ground placement back to the runway: `LU_MEMO_CHECK` timed out and was skipped, leaving BTC_AUTOBRK and
  BTC_SPLRS open, because the landing had disarmed the autobrake and the spoilers and the surveillance had gone to STBY: the A/BRK press, the
  spoiler arm and the MFD SURV page's DEFAULT SETTINGS (with its CONFIRM) cleared the memo. That is the Captain's work, as the checklist says.

## The re-check flights (2026-11-02 afternoon): what an OIS restore did to the aircraft

Three attempts to fly the Step 4 re-check crashed or ended off the runway, none of them on the First Officer's code:

- **The MCP placement into the air** (`msfs_set_aircraft_position`, 245 kt at 5,000 ft, 20 NM out on the runway 04 centreline): the A350 read
  30 kt 10 s later and fell into the sea (crash detection off). The same placement at FL200 on 2026-11-02 kept 102 kt. Not a way into the air.
- **In-air OIS restores** (the custom save P04 APPR and the 12:07:25 autosave, three times): AP 1 or AP 2 held altitude and speed but never a
  heading, in HDG, NAV or TRK: a right bank from about 22° crept to 45° over three minutes through every selected heading, with the ailerons and
  spoilers at neutral on the F/CTL page and in the sim's own variables, then the AP disconnected (MASTER WARNING, nothing sent by MSFSBA at that
  moment), the nose came up (to +6,000 ft/min) and the aircraft departed within 20 s. `AXIS_AILERONS_SET` and `AILERON_SET` moved nothing (the
  sim's yoke X stayed 0, on the ground too), while the elevator and rudder axes did; `REPAIR_AND_REFUEL` changed nothing. The joystick and
  throttle read centred.
- **The ground restore** (P05 RWY04, engines running): the same scripted take-off that flew the morning flight rejected itself at 107 kt, the
  cross-track readings jumping 20 m either side within seconds with almost no heading error; the idle command was not taken at first and the
  aircraft stopped 110 m right of the centreline, 2 km down the runway.
- **The fresh flight settles it:** with the hydraulics up and never restored, `AXIS_AILERONS_SET` +6000 moved the yoke X to -0.135 and the
  left aileron to 46% up (negative is a right roll, as measured on 2026-10-28), and the autopilot captured every heading. In the restored
  states the aileron channel read 0 whatever was sent: those restores came back without roll control.
- Every restore came after the first one of the day (P05 RWY04 at 12:40, before the MCP placement); the morning flight, never restored, flew
  normally from start to securing. A fresh flight loaded from the MSFS menu is needed for the re-check.
