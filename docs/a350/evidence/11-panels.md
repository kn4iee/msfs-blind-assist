# Plan 11: the panels

## Task 1: the control map from 1.2.8 (2026-11-03)

`generate_a350_map.py` on the installed 1.2.8 package against the committed 1.2.7 map (`diff_maps.py`): 700 controls (696), the
lamps unchanged, and one change, the four oxygen masks (`OXYGEN_TEST`, behaviour components `OXY_<seat>_*`):

| 1.2.7 input event | 1.2.8 input event | What it is (1.2.8 tooltip) | Key |
| --- | --- | --- | --- |
| `OXYTEST_<seat>_HATCH` | `OXY_<seat>_HATCH` | OXYGEN MASK, open/close (`INI_OXY_HATCH_<seat>`) | kept |
| `OXYTEST_<seat>_N` | `OXY_<seat>_N` | OXYGEN PERCENTAGE, NORMAL/100% (`INI_OXY_<seat>_N`) | kept |
| `OXYTEST_<seat>_PANEL` (hold) | `OXY_<seat>_MAIN` (hold) | OXYGEN MASK TEST, PUSH TO TEST: holds `INI_OXY_BLINKER_<seat>` | kept |
| `OXYTEST_<seat>_MASK` (hold, `INI_OXYTEST_EMER_<seat>`) | `OXY_<seat>_KNOB` (toggle) | EMER PRESSURE SEL, NORMAL/EMERGENCY: flips `INI_OXY_EMER_<seat>` | kept |
| none | `OXY_<seat>_KNOB_PUSH` (hold) | OXYGEN MASK TEST on the knob, PUSH TO TEST: holds `INI_OXY_BLINKER_<seat>` | new: `A350_OXY_<seat>_KNOB_PUSH` |

The seats are CPT, FO and the observers', TO and 4TH on 1.2.7, 1 and 2 on 1.2.8 (third and fourth occupant). The generator keeps
each same control's 1.2.7 key (`KEPT_KEYS`, 16 keys), so Ctrl+M settings and tests pinning them still find them; the four knob
pushes are the only new keys, and no key was lost. The flow blinkers (MSFSBA's own indicators, no lamp node) read
`INI_OXY_BLINKER_<seat>` now and keep their 1.2.7 light keys; the latched holds (`A350WritePlan.Latched`) are MAIN and KNOB_PUSH,
the emergency selector no longer a hold.

Live on 1.2.8 through MSFSBA's rows, each seat (scratchpad `oxy_walk.sh`): stowage Closed to Open and back, N/100% Normal to 100%
and back, emergency pressure Normal to Emergency and back, each read back from its `INI_OXY_*` variable and said ("Third occupant
oxygen emergency pressure: Emergency"); the mask test and the knob's push each held the flow blinker at 1 and let it go to 0,
"First officer oxygen flow blinker on", "... off".

## Task 2: the split panels and the SND keys (2026-11-03, on the stand)

`A350Probe live` over every split panel's three parts (Wipers, Warnings, HUD, HUD Units, Loudspeakers and Table Lights, Display
Power, ECAM Control Panel, Weather Radar, Display Transfer and the shared Cockpit panel, Sun Shades, Tillers and Sidesticks; the
oxygen masks in Task 1): every set row PASS at every position and restored (25 rows), every press pressed and read back; the knobs
(loudspeakers, table lights, dome, flood and integral lights, the main sun shades) are sliders the pass leaves to the hand. Each
row's name carries its side ([VAR-1]); the captain's master lights speak as "Master caution" and "Master warning", and one rule
lights both autoland and both ATC MSG lights, so the captain's speaks for both (`MasterSpeakers`, `_twinEchoes`), by design.

- The weather radar's ELEVN, GAIN and VD AZIM knobs (each side): their push and pull say "auto"/"manual pressed", their turns
  read back the knob ("Captain weather radar elevation: 0"). The value never moves, in AUTO or MANUAL: the turn's code pulses
  `INI_WXR_ELEV_LEFT_UP`/`_DN` (and GAIN's, VD AZIM's), which nothing in the module reads (its radar keeps only `INI_WXR_TILT`,
  `INI_WXR_GAIN`, `INI_WXR_MODE`, `INI_WXR_SYS`, `INI_WXR_ON`). Not modelled on 1.2.8, as the handoff's list had it.
- WXR SYS 1 and 2 light their own key (`INI_WXR_SYS`); the sweep left SYS 2, put back to 1.
- iniBuilds' own lamp rules light ADR 2's and ADR 3's FAULT from `INI_ADF2_FAULT` and `INI_ADF3_FAULT`: MSFSBA shows what the
  cockpit lights.
- The SND (option fitted, `INI_OPTION_FO_ISIS` 1) on the stand reads ground speed, heading source, position and its source. MENU
  opens its menu (`INSERT WPT`, cyan; MENU again closes it); MODE, plus, minus, LS/DIR TO and STD/CHANGE changed nothing in its
  draw (the same texts and shapes before and after each). The module's SND draws pseudo-waypoints and has a menu handler: the
  keys are to be tried in flight with a route; their names stay the placard words until then.

## Task 3: the gear and IRS read-outs (2026-11-03)

- On the stand, gear down: `INI_GEAR0_POSITION`, `INI_GEAR1_POSITION`, `INI_GEAR2_POSITION` read 50 (`INI_GEAR3_POSITION` 0;
  stock `GEAR * POSITION` 100). MSFSBA reads none of them: the gear key says the lever, the SD WHEEL page's reader the gear
  symbols. In transit and UNLK: in flight.
- A real alignment from OFF through MSFSBA's ADIRS rows (all three OFF, 20 s, NAV; scratchpad `irs_align.sh`): the E/WD said
  "IR IN ALIGN 6 MN"; `INI_IRSn_TIME_REMAIN` counts seconds, 294 at NAV and down one a second; all three `INI_IRSn_ALIGNED` and
  `INI_IRS_ALIGNED` came up together about five minutes on, and MSFSBA said "IRS aligned" once. A flight load's aligned IRs are
  the baseline (`A_flight_loaded_aligned_is_its_baseline`).

## Task 4: the panel backlog (2026-11-03)

- Trims (owner's backlog item 3): the pitch trim is said by the shared "Trim up 1.4" (`MON_ElevatorTrim`, Shift+T); the rudder trim
  now too, "Rudder trim 0.5 degrees left" (`A350RudderTrimCallout`, on the "Rudder trim" line's Ctrl+M row). On the stand with the
  engines off neither trim moves (the nudges press, `RUDDER_TRIM_ACTUAL_ANGLE` stays 0): heard in flight.
- Chronos (owner's item 18): each side's key starts, stops and resets its chrono (reset reads 0 and the ND shows none). The ND
  draws the time as two bare numbers ("Chrono: 00 29"), minutes and seconds or hours and minutes alike; each chrono's own
  seconds (`INI_CPT_CHRONO`, `INI_FO_CHRONO`) are now a status line beside its key in the A380's words ("1 minute 3 seconds").
- Oxygen masks, all four seats: Task 1.
