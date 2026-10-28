# BlueBird 757 panels: do they follow the cockpit? (research, plan 12)

Date: 2026-10-28. Read-only research: no repository file but this report was written, the simulator was not touched.

## The answer in short

Most of the app's 757 panels are real cockpit panels: 34 of the 55 map panels hold controls of one physical panel only,
in the right section, and 24 of those are that panel complete (APU, Hydraulics, Fuel, Window heat, the MCP, the radio
tuning and audio panels, the P61 panel and so on); the other 10 are halves of a panel cut in two or miss a control
filed elsewhere. Of the other 21, seven are sensible groupings of nearby or paired panels (Engines, Electrical, Air
conditioning and bleed, Exterior lights, VOR/DME, Master warning and caution, Yokes), two join a neighbouring plate
that does not belong ("Engine control and yaw damper" joins the RR engine control switches to the unrelated yaw
dampers; "Pressurization" holds the equipment cooling switch from the plate below it), and the rest fall in three
kinds:

- **Made up (code buckets).** "Main panel switches" (39 controls spread over the whole 1.8 m width of the main
  instrument panel, from 13 separate cockpit panels), "Overhead miscellaneous" (three different overhead panels),
  "Switch guards" (seven guards, each taken off the switch it covers), "Display brightness" (eight knobs on five
  different panels, none of them next to its display) and "Cockpit miscellaneous" (the flight deck door itself). These
  come from the way BlueBird's code file is split into sections, not from the cockpit.
- **In the wrong place.** "Captain EFIS" and "First officer EFIS" are listed under Forward panel but sit on the
  pedestal; "Oxygen" is under Forward panel but its controls are the four oxygen mask boxes at the four seats; "Cabin
  lights" and "Ground service" are under Overhead but sit on the forward attendant panel in the cabin by the left
  front door, 3.1-3.6 m behind the overhead. Single controls in the wrong panel: the first officer's chronometer CLOCK
  switch is in "Captain chronometer"; the four fire and overheat test buttons are with the fire handles ("Fire
  protection") though they sit at the other end of the aisle stand; the app's rudder and aileron trim buttons are in
  "Stabilizer trim" though those switches sit on their own trim panel at the back of the aisle stand.
- **One real panel cut in two.** IRS / IRS keypad, Weather radar left / right, Flight deck door / Warning lights test
  (these six halves are among the 34 above), and Cabin lights / Ground service (also in the wrong section).

The sensible groupings (Engines: start selectors on the overhead with the fuel control switches on the pedestal, as
the owner accepts; the two VOR/DME panels; the two master warning lights; the two yokes; Electrical; Air conditioning
and bleed; Exterior lights) are kept in the proposal below. The proposal moves 142 of the 409 map controls and
goes from 55 to 64 map panels (plus the app's own "COM radios" and a new app panel "Rudder and aileron
trim").

## Method

1. **Positions.** The interior models `SimObjects/Airplanes/bbs-757-200-rr/model/BBS_752_RR_INTERIOR.gltf` (3,116
   nodes) and `.../bbs-757-200-pw/model/BBS_752_PW_INTERIOR.gltf` (3,104 nodes) were walked from the scene roots,
   composing each node's translation, rotation and scale (or matrix). A node's position is the centre of the world
   bounding box of its own mesh (the POSITION accessor's min/max, transformed), or of its children's meshes; the bare
   node origin is misleading for some nodes (the four `O2_EARS_*` origins all sit at the pedestal, their meshes at the
   four seats). All 409 map nodes were found. The 401 nodes in both models sit within 7 mm of each other; the RR-only
   nodes (ELEC ENG CONT and its guards, the RR fuel control switches) come from the RR model, the PW fuel control
   switches from the PW model (they sit where the RR ones do).
2. **Axes** (metres, model frame): **x** positive towards the captain's side (left), negative towards the first
   officer's; **y** up; **z** positive forward. For orientation: the main instrument panel's controls are at z 20.70-20.81,
   y 0.97-1.26; the MCP at z 20.60-20.64, y 1.39-1.44; the overhead runs from z 20.02 (aft, y 1.98) to z 20.54
   (forward, y 1.76); the pedestal top is at y 0.82-0.97 from z 20.68 (just below the EICAS displays) back to z 19.80;
   the observer seats' walls at z 19.1-19.4; the flight deck door at z 18.48; the forward attendant panel at z 16.94.
3. **Grouping by position.** Each node was put into an area by its position and, inside an area, into a cluster: a
   physical panel shows as a group of controls a few centimetres apart with a gap to the next group. Clusters were
   named from BlueBird's labelled cockpit pictures and procedures. Scripts and the full per-node table are in the
   scratchpad (paths at the end).
4. **Manual.** "manual p.N" is the printed page N of `D:\Documents\Bleubird Manuals\BlueBird_757_Complete_Flight_Guide_v1.01.pdf`
   (printed 33A-33E are PDF pages 34-38; from printed p.39 on, PDF page = printed + 5). The labelled pictures used:
   p.33A Overhead panel, p.33B Main instrument panel, p.33C Mode control panel, p.33D Glareshield lighting controls,
   p.33E Aisle stand panels; also p.95 (oxygen mask box), p.102 (EICAS control panel), p.103 (EFIS control panel),
   p.104 (thrust quadrant). The User Procedures PDF repeats these procedure pages (its PDF pages are 6 lower); the
   Normal Checklist names no panels.
5. **Vendor banners.** For each node the code-file section of `BBS_752_BASE_INTERIOR.xml` it sits in
   (`docs/b757/evidence/controls/control-map.tsv`, columns panel/section) and the app's curation rule
   (`tools/b757-gen/curation.json`, `panels`, `node_panels`) were read to say where each grouping came from.
6. **Marks.** "verified (position)" is from the computed positions; "verified (manual p.N)" is read on that page or
   picture; "(general)" is general 757 knowledge not checked against BlueBird.

Limits: the manual's overhead picture (p.33A) shows an overhead without the RR-only ELEC ENG CONT and engine limiter
switches, so their panel names come from position and from the procedure text (manual p.39 "EEC SWITCHES (RR ONLY)",
"ENGINE LIMITERS (RR ONLY)"), not from a picture label.

## The cockpit's real panels (where the map's controls sit)

- **Overhead** (verified manual p.33A, positions): left column IRS mode select unit (display knobs, keypad, three
  mode selectors), RR electronic engine control, yaw dampers, hydraulics; next column battery and standby power,
  electrical power, APU, cockpit voice recorder, panel/flood lighting with LT OVRD and the runway turnoff switches;
  centre column emergency lights / passenger oxygen, ram air turbine (with the RR engine limiters on the same row,
  verified position), engine start, fuel, anti-ice, rain repellent / wiper, exterior lighting; next column window
  heat, SELCAL / cabin calls, passenger signs, pressurization (cabin altitude control), equipment cooling, and the
  lighting / flight deck door panel (indicator lights test and brightness, door lock, logo light); right column air
  conditioning and bleed air.
- **Glareshield** (verified manual p.33C, p.33D): MCP with the left and right VOR/DME tuning panels at its ends; a
  master warning/caution light each side, outboard of the VOR/DME panels; a glareshield lighting panel at each outboard end with a CLOCK switch.
- **Main instrument panel** (verified manual p.33B): at each outboard end foot/shoulder heat and the instrument source
  selectors; the captain's and first officer's flight instruments (DDRMI, clock, autoland status); in the centre panel
  the standby instruments (with the standby engine indicator), brake controls (autobrake, reserve brakes), the caution
  cancel/recall and T/O CHK buttons and the G/S inhibit, the heading reference switch, the thrust mode select panel,
  the gear lever with the alternate gear extension, alternate flaps, and the ground proximity override switches.
- **Pedestal, forward part** (verified position; manual p.102, p.103): CDU 1 and CDU 2 side by side, the EICAS
  control panel between them, level with their forward ends (just below the lower EICAS display; manual p.33B labels it "EICAS
  DISPLAY CONTROLS"), and an EFIS control panel behind each CDU.
- **Control stand / thrust quadrant** (verified position; manual p.104): thrust levers with TO/GA and A/T disconnect,
  speedbrake, flap lever, parking brake, fuel control switches, and on its left side the stab trim cutout and
  alternate stab trim switches.
- **Aisle stand** (verified manual p.33E): fire extinguishing panel (engine and APU handles, cargo fire), weather radar
  (one panel, left and right mode rows), VHF L/R/C radio tuning, left and right audio selector panels, ADF tuning,
  transponder/TCAS, ILS tuning, aileron/rudder trim, fire/overheat test at the aft end.
- **Behind the seats and in the cabin** (verified position): an oxygen mask box at each of the four seats (captain's
  and first officer's beside the outboard ends of the main panel, the first observer's on the left wall, the second
  observer's on the right wall under the P61 panel); the P61 panel on the right sidewall behind the first officer
  (manual p.76-77); the flight deck door in the aft bulkhead; the forward attendant panel aft of door 1L (the 757's
  forward attendant panel holds cabin lighting and the ground service switch: (general)).

## Verdict per app panel

"Real" = the controls of one physical panel. "Grouping" = nearby or paired panels joined for a reason. "Made up" = a
code bucket that mixes places or splits a real panel. "Split" = part of a real panel whose other part is in another
app panel. Sources: the vendor banner and curation rule each grouping came from.

| App panel (section) | Rows | Verdict | Why (evidence) |
|---|---|---|---|
| Electrical (Overhead) | 12 | grouping | Battery and standby power plate + electrical power plate, adjacent, one system, consecutive items of one flow (verified manual p.33A, p.39). Its three guards (battery, L/R gen drive) are in Switch guards. Banner "ELECTRICAL PANEL". |
| APU (Overhead) | 1 | real | verified manual p.33A. |
| IRS (Overhead) | 5 | split | Same IRS mode select unit as IRS keypad: the keypad is 5 cm from the display knobs (verified position, manual p.33A one "IRS" panel). The split comes from two vendor banners ("IRS KNOBS", "IRS SUB-CATEGORY - BUTTON_GLASS"). |
| Engine control and yaw damper (Overhead) | 4 | grouping, unrelated | RR ELEC ENG CONT plate (between IRS and yaw dampers, RR model only) + YAW DAMPERS plate (verified position, manual p.33A, p.39). Banner "BUTTONS AND SWITCHES MAIN". The two EEC guards are in Switch guards. |
| Hydraulics (Overhead) | 6 | real | verified manual p.33A. |
| Overhead miscellaneous (Overhead) | 7 | made up | Banner "LIGHT OVERRIDE" (a code section). Three plates: emergency lights / passenger oxygen (aft end of centre column), RAT row with the RR engine limiters, and LT OVRD on the panel/flood lighting plate at the forward left, 0.5 m from the emergency lights plate (verified position, manual p.33A). |
| Fuel (Overhead) | 8 | real | verified manual p.33A. |
| Anti-ice (Overhead) | 3 | real | verified manual p.33A. |
| Wipers (Overhead) | 1 | real | Rain repellent / wiper panel (verified manual p.33A); the rain repellent buttons are not in the map. |
| Exterior lights (Overhead) | 10 | grouping | 7 on the exterior lighting plate; L/R runway turnoff on the panel/flood lighting plate 6-8 cm to the left; logo on the lighting / flight deck door plate to the right, 6 cm away (verified position, manual p.33A). Sensible: all exterior lights in one place. Curation `node_panels` moved the logo in. |
| Window heat (Overhead) | 4 | real | verified manual p.33A. |
| Passenger signs (Overhead) | 2 | real | verified manual p.33A. |
| Pressurization (Overhead) | 3 | mostly real | Mode Select and Auto Rate on the cabin altitude control; Equip is the separate EQUIPMENT COOLING plate just below it (verified manual p.33A, position: 6 cm). Banner "(after PRESSURIZATION CONTROL PANEL)". The app's rows (landing altitude, cabin climb/descend, outflow valve) are on the real panel (verified position: `B757_OVHD_LDG_ALT_KNOB` (-0.12, 1.83, 20.35), `B757_OVHD_CABINALT_SWITCH` (-0.15, 1.86, 20.30)). |
| Air conditioning and bleed (Overhead) | 14 | grouping | Air conditioning plate + bleed air plate in the same column (verified manual p.33A). Sensible: one system, one flow block (manual p.40). |
| Cabin lights (Overhead) | 4 | wrong section, split | Not on the overhead: the forward attendant panel by door 1L, x 1.07..1.42, y 2.0, z 16.94, 3.1-3.6 m behind the overhead (verified position). Ground service is on the same panel, 3-33 cm away. Curation `node_panels` rule `B757_CABIN_` out of banner "lights". |
| IRS keypad (Overhead) | 12 | split | See IRS. |
| Switch guards (Overhead) | 7 | made up | Banner "PLASTIC BUTTON and SWITCH GUARDS". Each guard sits on its switch (bounding-box centres 0.1-0.4 cm apart, verified position) on four different plates; none is with its switch in the app. |
| Flight deck door (Overhead) | 1 | split | The door lock selector is on the lighting / flight deck door plate (verified manual p.33A), 3 cm from Warning Test (verified position). Banner "TEST WARNING LIGHTS PANEL"; curation moved it out. |
| CVR (Overhead) | 2 | real | verified manual p.33A. |
| SELCAL and cabin call (Overhead) | 11 | real | verified manual p.33A. |
| Ground service (Overhead) | 1 | wrong section, split | Forward attendant panel (verified position (1.39, 1.99, 16.94)); see Cabin lights. |
| Warning lights test (Overhead) | 2 | split | Indicator lights TEST and DIM/BRT on the lighting / flight deck door plate (verified manual p.33A). |
| MCP (Glareshield) | 23 | real | verified manual p.33C. |
| VOR/DME (Glareshield) | 2 | grouping | Left and right VOR/DME tuning panels at the two ends of the MCP, 0.58 m apart (verified manual p.33C). Sensible pair. |
| Master warning and caution (Glareshield) | 2 | grouping | One each side, outboard of the VOR/DME panels, 0.78 m apart (verified position, manual p.33C picture). Sensible pair. |
| Main panel switches (Forward panel) | 39 | made up | Banner "MAIN PANEL SWITCH GEAR + BUTTONS". Spread 1.81 m over 13 physical panels: both outboard instrument source selector panels and foot/shoulder heat, brake controls, caution cancel/recall, T/O CHK, G/S inhibit, the standby engine indicator switch, heading reference, thrust mode select, alternate gear (cut off from the gear lever), alternate flaps, ground proximity overrides (verified manual p.33B, position). |
| Landing gear (Forward panel) | 2 | real, incomplete | Gear lever and lock override; the alternate gear extension switch and guard, 6 cm from the gear lock override and 15 cm below the gear lever, are in Main panel switches (verified position; manual p.101 "alternate extension guarded"). |
| Captain EFIS (Forward panel) | 12 | real panel, wrong section | On the pedestal behind CDU 1, y 0.88, z 20.37-20.47 (verified position; banner "... B757_AISLE_STAND_LCD ..."). Its ADI and HSI brightness knobs (manual p.103) are in Display brightness. |
| Yokes (Forward panel) | 2 | grouping | One A/P disconnect on each yoke (verified position). Sensible pair. |
| Captain autoland status (Forward panel) | 3 | real | verified position, manual p.33B. |
| Standby attitude indicator (Forward panel) | 2 | real, incomplete | The standby engine indicator switch (Stdby EICAS, 14 cm away in the same standby instruments column) is in Main panel switches (verified position, manual p.33B "STANDBY INSTRUMENTS", p.100). |
| Captain DDRMI (Forward panel) | 2 | real | verified position. |
| Captain chronometer (Forward panel) | 6 | mostly real | Four clock controls + both glareshield CLOCK switches (manual p.33D, p.146 "glareshield CLOCK button"). The captain's CLOCK switch with the captain's clock is a sensible remote-start grouping; the first officer's (tooltip "First Officer Clock Start/Stop", at (-0.76, 1.38, 20.54): 1.17 m from the captain's clock, 0.39 m from the first officer's) belongs with the first officer's clock. |
| Oxygen (Forward panel) | 12 | grouping, wrong section | The four oxygen mask boxes at the four seats, spread 2.69 m; none is on the forward panel (verified position; manual p.95). Curation `node_panels` rules `OXY_`, `BBS757_O2_`, `O2_EARS_` with section Forward panel. |
| Cockpit miscellaneous (Forward panel) | 1 | made up, wrong section | The flight deck door itself (door leaf 1.9 m tall at z 18.48, 2.3 m behind the main panel, verified position). Banner "CHANGE MAJOR CODE and UPDATES BELOW". |
| First officer EFIS (Forward panel) | 12 | real panel, wrong section | As Captain EFIS, behind CDU 2. |
| First officer autoland status (Forward panel) | 3 | real | verified position. |
| First officer DDRMI (Forward panel) | 2 | real | verified position. |
| First officer chronometer (Forward panel) | 4 | real, incomplete | Its glareshield CLOCK switch is in Captain chronometer. |
| Display brightness (Forward panel) | 8 | made up (app-made), wrong section | Plan 12's own grouping. None is near its display: all eight are on the pedestal's forward part. PFD (ADI) and ND (HSI) knobs on each EFIS control panel (verified manual p.103 "Top: DH REF and ADI brightness. Center: ... HSI brightness", position: 3-4 cm from that panel's other controls); Upper/Lower EICAS = the concentric BRT knob of the EICAS control panel (verified manual p.102, position: 4 cm from its other controls); CDU knobs on each CDU face (verified position). |
| Control stand (Pedestal) | 7 | real | verified position, manual p.104. The fuel control switches (in Engines) and the stab trim switches (in Stabilizer trim) are on the same control stand. |
| Engines (Pedestal) | 7 | grouping (accepted) | Ignition and both start selectors on the overhead engine start panel + the fuel control switches on the control stand, 0.99 m apart (verified position, manual p.33A, p.104). The owner accepts this. |
| TCAS and transponder (Pedestal) | 4 | real | verified manual p.33E. |
| EICAS control (Pedestal) | 7 | real, incomplete | verified manual p.102; its BRT knob (Upper/Lower EICAS) is in Display brightness. |
| Radio tuning panel 1 (Pedestal) | 9 | real | VHF L radio tuning control panel (verified manual p.33E, position: captain side, forward). |
| Audio panel 1 (Pedestal) | 25 | real | Audio selector panel left (verified manual p.33E). |
| Fire protection (Pedestal) | 19 | grouping | Fire extinguishing panel (15) + fire/overheat test panel (4) at the other end of the aisle stand, 0.34-0.44 m aft (verified manual p.33E, position). Banner "PEDESTAL GAURD SWITCH GEAR". |
| Stabilizer trim (Pedestal) | 4 (+ 6 app buttons, 2 readouts) | real for its map rows | The cutouts and the alternate stab trim switch are on the control stand's left side (verified position, manual p.104 picture). The app's rudder and aileron trim buttons are not: those switches are on the aileron/rudder trim panel at the aft end of the aisle stand, 0.40-0.55 m aft (verified position, manual p.33E). |
| Weather radar left (Pedestal) | 6 | split | One weather radar panel with a LEFT MODE row, a RIGHT MODE row and one TEST (verified manual p.33E); the shared TEST is filed under left. |
| Radio tuning panel 2 (Pedestal) | 9 | real | VHF R (verified manual p.33E). |
| Radio tuning panel 3 (Pedestal) | 9 | real | VHF C (verified manual p.33E, position: captain side, aft). |
| Audio panel 2 (Pedestal) | 25 | real | Audio selector panel right (verified manual p.33E). |
| Weather radar right (Pedestal) | 5 | split | See Weather radar left. |
| Pedestal radio panels (Pedestal) | 1 | real, misnamed | Its one row is the TEST button of the ILS tuning panel (verified manual p.33E, position). Banner "INDIVIDUAL AUDIO/RADIO PANELS". |
| P61 panel (Other) | 15 | real | Right sidewall behind the first officer (verified position, manual p.76-77). The app's 16 P61 test buttons are on it too (switches `B757_VC_P61_SWITCH_3..10` at x -0.79, y 1.96-2.01, z 19.26-19.36, verified position). |
| COM radios (app, Pedestal) | 6 fields | grouping | The three VHF radios' frequency fields (`BBS757Definition.Radios.cs`); physically on the three radio tuning panels. Sensible: one place to tune. |

## The panels asked about

- **Main panel switches**: made up (above). Split into 10 new forward-panel panels; its alternate gear pair joins
  Landing gear and its Stdby EICAS joins Standby instruments.
- **Overhead miscellaneous**: made up. Emergency lights, its guard and passenger oxygen are one plate; RAT and the RR
  engine limiters share the row below it; LT OVRD belongs to panel/flood lighting.
- **Switch guards**: made up; every guard is separated from its switch (table below). The other twelve guards in the
  map (emergency lights, alternate gear, the three GPWS overrides, the two cargo bottles, the two stab trim cutouts,
  the three P61 glass guards) are already with their switches.
- **Engine control and yaw damper**: two unrelated adjacent plates; split.
- **Engines**: grouping the owner accepts; kept.
- **Oxygen**: one function at four seats; kept as one panel but moved out of Forward panel. Label note below.
- **Cockpit miscellaneous**: the door itself; becomes "Flight deck door" under Other.
- **Ground service**, **Cabin lights**: one forward attendant panel in the cabin; merged, moved to Other.
- **Flight deck door** (overhead): the door lock selector; merged with Warning lights test, its own plate.
- **Pedestal radio panels**: the ILS tuning panel; renamed.
- **IRS vs IRS keypad**: one unit; merged (or keep the keypad as its own panel only to keep IRS short: see open questions).
- **Display brightness**: made up; not near the displays; every knob goes to the panel it is on.
- **Stabilizer trim**: its map rows and the alternate stab trim buttons are right; the rudder and aileron trim buttons
  move to a new "Rudder and aileron trim" panel at the aft aisle stand, with the rudder and aileron trim readouts.
- **Control stand**: real.
- **Cabin lights**: see Ground service.
- **Warning lights test**: split of the lighting / flight deck door plate; merged.
- **Exterior lights**: grouping kept (runway turnoff and logo sit on neighbouring plates, 6-8 cm away).
- **EICAS control**: real; gains its BRT knob.

## Controls in an app panel other than their physical panel (71)

Positions are bounding-box centres, (x, y, z) in metres, axes as in Method. The Engines grouping is not listed (accepted).
Each row is verified (position); the evidence column gives the manual page that names the physical panel.

| App panel now | Label | Node | Position (x, y, z) m | Physically on | Proposed panel | Evidence |
|---|---|---|---|---|---|---|
| Overhead miscellaneous | Emergency Lights | `B757_OVHD_PASS_OXY_Toggle_Switch_CLICK` | (0.03, 1.98, 20.04) | Emergency lights / passenger oxygen (Overhead) | Emergency lights and passenger oxygen | manual p.33A "EMERGENCY LIGHTS / PASSENGER OXYGEN" |
| Overhead miscellaneous | Emergency Lights Guard | `B757_OVHD_SWITCH_GAURD` | (0.04, 1.97, 20.03) | Emergency lights / passenger oxygen (Overhead) | Emergency lights and passenger oxygen | manual p.33A "EMERGENCY LIGHTS / PASSENGER OXYGEN" |
| Overhead miscellaneous | Lt Ovrd | `B757_OVHD_LT_OVRD_BUTTON` | (0.11, 1.78, 20.48) | Panel/flood lighting and runway turnoff (Overhead) | Panel lighting | manual p.33A "PANEL / FLOOD LIGHTING", "RUNWAY TURNOFF LIGHTS"; manual p.39 |
| Overhead miscellaneous | L ENG Limiter | `B757_OVHD_L_ENG_LIMITER_BUTTON` | (-0.01, 1.96, 20.08) | Ram air turbine row (RAT, RR engine limiters) (Overhead) | Ram air turbine and engine limiters | manual p.33A "RAM AIR TURBINE"; limiters: position |
| Overhead miscellaneous | R ENG Limiter | `B757_OVHD_R_ENG_LIMITER_BUTTON` | (-0.04, 1.96, 20.08) | Ram air turbine row (RAT, RR engine limiters) (Overhead) | Ram air turbine and engine limiters | manual p.33A "RAM AIR TURBINE"; limiters: position |
| Overhead miscellaneous | Passenger Oxygen | `B757_OVHD_PASS_OXY_BUTTON` | (-0.04, 1.98, 20.03) | Emergency lights / passenger oxygen (Overhead) | Emergency lights and passenger oxygen | manual p.33A "EMERGENCY LIGHTS / PASSENGER OXYGEN" |
| Overhead miscellaneous | RAT | `B757_OVHD_RAT_BUTTON` | (0.04, 1.96, 20.08) | Ram air turbine row (RAT, RR engine limiters) (Overhead) | Ram air turbine and engine limiters | manual p.33A "RAM AIR TURBINE"; limiters: position |
| Exterior lights | L Runway Turnoff | `B757_OVHD_RWY_TURN_L` | (0.13, 1.76, 20.51) | Panel/flood lighting and runway turnoff (Overhead) | Exterior lights | manual p.33A "PANEL / FLOOD LIGHTING", "RUNWAY TURNOFF LIGHTS"; manual p.39 |
| Exterior lights | R Runway Turnoff | `B757_OVHD_RWY_TURN_R` | (0.10, 1.76, 20.51) | Panel/flood lighting and runway turnoff (Overhead) | Exterior lights | manual p.33A "PANEL / FLOOD LIGHTING", "RUNWAY TURNOFF LIGHTS"; manual p.39 |
| Exterior lights | Logo Lights | `B757_OVHD_LOGO_BUTTON` | (-0.10, 1.77, 20.50) | Lighting / flight deck door (Overhead) | Exterior lights | manual p.33A "LIGHTING / FLIGHT DECK DOOR" |
| Pressurization | Equip | `B757_OVHD_EQUIP_BUTTON` | (-0.15, 1.82, 20.40) | Equipment cooling (Overhead) | Equipment cooling | manual p.33A "EQUIPMENT COOLING" |
| Switch guards | Battery Guard | `B757_GAURD_3_SWITCH` | (0.18, 1.91, 20.19) | Battery and standby power (Overhead) | Electrical | manual p.33A "BATTERY & STANDBY POWER" |
| Switch guards | Elec Eng Cont 1 Guard (RR) | `B757_GAURD_1_SWITCH` | (0.32, 1.92, 20.16) | Electronic engine control (RR) (Overhead) | Electronic engine control (RR) | position (RR model only); manual manual p.39 "EEC SWITCHES (RR ONLY)" |
| Switch guards | Elec Eng Cont 2 Guard (RR) | `B757_GAURD_2_SWITCH` | (0.27, 1.92, 20.16) | Electronic engine control (RR) (Overhead) | Electronic engine control (RR) | position (RR model only); manual manual p.39 "EEC SWITCHES (RR ONLY)" |
| Switch guards | Passenger Oxygen Guard | `B757_GAURD_6_SWITCH` | (-0.04, 1.98, 20.03) | Emergency lights / passenger oxygen (Overhead) | Emergency lights and passenger oxygen | manual p.33A "EMERGENCY LIGHTS / PASSENGER OXYGEN" |
| Switch guards | Ram Air Turbine Guard | `B757_GAURD_7_SWITCH` | (0.04, 1.96, 20.08) | Ram air turbine row (RAT, RR engine limiters) (Overhead) | Ram air turbine and engine limiters | manual p.33A "RAM AIR TURBINE"; limiters: position |
| Switch guards | Left Generator Drive Disconnect Guard | `B757_GAURD_4_SWITCH` | (0.17, 1.85, 20.34) | Electrical power (Overhead) | Electrical | manual p.33A "ELECTRICAL POWER" |
| Switch guards | Right Generator Drive Disconnect Guard | `B757_GAURD_5_SWITCH` | (0.13, 1.85, 20.34) | Electrical power (Overhead) | Electrical | manual p.33A "ELECTRICAL POWER" |
| Main panel switches | Autobrake | `B757_MAIN_knob_gears_altn_autobrake` | (0.15, 1.01, 20.72) | Brake controls (Forward panel) | Brakes | manual p.33B "BRAKE CONTROLS" |
| Main panel switches | Recall | `B757_MAIN_RECALL_BUTTON` | (0.11, 1.25, 20.81) | Caution cancel / recall (center panel, top) (Forward panel) | Caution, config and G/S inhibit | position; manual p.158 "CAUTION CANCEL and RECALL" |
| Main panel switches | Cancel | `B757_MAIN_CANCEL_BUTTON` | (0.13, 1.25, 20.81) | Caution cancel / recall (center panel, top) (Forward panel) | Caution, config and G/S inhibit | position; manual p.158 "CAUTION CANCEL and RECALL" |
| Main panel switches | Takeoff Check | `B757_MAIN_TOCHK_BUTTON` | (0.31, 1.25, 20.80) | T/O CHK button (center panel, top left) (Forward panel) | Caution, config and G/S inhibit | position; manual p.158 "main-panel control is marked T/O CHK" |
| Main panel switches | L Foot Heaters | `B757_MAIN_L_FOOT_HEATERS_SWITCH` | (0.91, 1.02, 20.71) | Captain foot / shoulder heat (Forward panel) | Captain foot and shoulder heat | manual p.33B "FOOT / SHOULDER HEAT" |
| Main panel switches | R Foot Heaters | `B757_MAIN_R_FOOT_HEATERS_SWITCH` | (-0.90, 1.02, 20.71) | First officer foot / shoulder heat (Forward panel) | First officer foot and shoulder heat | manual p.33B "FOOT / SHOULDER HEAT" |
| Main panel switches | L Shoulder Heaters | `B757_MAIN_L_SHOULDER_HEATERS_SWITCH` | (0.91, 0.99, 20.70) | Captain foot / shoulder heat (Forward panel) | Captain foot and shoulder heat | manual p.33B "FOOT / SHOULDER HEAT" |
| Main panel switches | R Shoulder Heaters | `B757_MAIN_R_SHOULDER_HEATERS_SWITCH` | (-0.90, 0.99, 20.70) | First officer foot / shoulder heat (Forward panel) | First officer foot and shoulder heat | manual p.33B "FOOT / SHOULDER HEAT" |
| Main panel switches | L Flt Dir | `B757_MAIN_L_FLT_DIR_SWITCH` | (0.82, 1.13, 20.76) | Captain instrument source selectors (Forward panel) | Captain instrument source selectors | manual p.33B "INSTRUMENT SOURCE SELECTORS"; manual p.96 "Captain INSTR SOURCE SEL" |
| Main panel switches | R Flt Dir | `B757_MAIN_R_FLT_DIR_SWITCH` | (-0.83, 1.13, 20.76) | First officer instrument source selectors (Forward panel) | First officer instrument source selectors | manual p.33B "INSTRUMENT SOURCE SELECTORS" |
| Main panel switches | L Flt Nav | `B757_MAIN_L_FLT_NAV_SWITCH` | (0.82, 1.08, 20.74) | Captain instrument source selectors (Forward panel) | Captain instrument source selectors | manual p.33B "INSTRUMENT SOURCE SELECTORS"; manual p.96 "Captain INSTR SOURCE SEL" |
| Main panel switches | R Flt Nav | `B757_MAIN_R_FLT_NAV_SWITCH` | (-0.83, 1.08, 20.74) | First officer instrument source selectors (Forward panel) | First officer instrument source selectors | manual p.33B "INSTRUMENT SOURCE SELECTORS" |
| Main panel switches | Stdby EICAS | `B757_MAIN_STDBY_EICAS_KNOB` | (0.12, 1.07, 20.74) | Standby instruments (Forward panel) | Standby instruments | manual p.33B "STANDBY INSTRUMENTS"; manual p.100 |
| Main panel switches | HDG Ref | `B757_MAIN_HDG_REF_SWITCH` | (-0.12, 1.25, 20.79) | Heading reference (center panel, top right) (Forward panel) | Heading reference | position |
| Main panel switches | TOGA | `B757_MAIN_TOGA_BUTTON` | (-0.12, 1.21, 20.79) | Thrust mode select (Forward panel) | Thrust mode select | manual p.33B "THRUST / GEAR / FLAP CONTROLS" |
| Main panel switches | TMSP 1 | `B757_MAIN_1_BUTTON` | (-0.15, 1.21, 20.79) | Thrust mode select (Forward panel) | Thrust mode select | manual p.33B "THRUST / GEAR / FLAP CONTROLS" |
| Main panel switches | TMSP 2 | `B757_MAIN_2_BUTTON` | (-0.17, 1.21, 20.79) | Thrust mode select (Forward panel) | Thrust mode select | manual p.33B "THRUST / GEAR / FLAP CONTROLS" |
| Main panel switches | CLB | `B757_MAIN_CLB_BUTTON` | (-0.14, 1.21, 20.79) | Thrust mode select (Forward panel) | Thrust mode select | manual p.33B "THRUST / GEAR / FLAP CONTROLS" |
| Main panel switches | CRZ | `B757_MAIN_CRZ_BUTTON` | (-0.14, 1.18, 20.78) | Thrust mode select (Forward panel) | Thrust mode select | manual p.33B "THRUST / GEAR / FLAP CONTROLS" |
| Main panel switches | G/S Inhibit | `BBS757_GS_INHIBIT` | (0.12, 1.16, 20.77) | G/S inhibit (center panel, left) (Forward panel) | Caution, config and G/S inhibit | position |
| Main panel switches | L EFI | `B757_MAIN_L_EFI_BUTTON` | (0.83, 1.03, 20.73) | Captain instrument source selectors (Forward panel) | Captain instrument source selectors | manual p.33B "INSTRUMENT SOURCE SELECTORS"; manual p.96 "Captain INSTR SOURCE SEL" |
| Main panel switches | R EFI | `B757_MAIN_R_EFI_BUTTON` | (-0.83, 1.03, 20.73) | First officer instrument source selectors (Forward panel) | First officer instrument source selectors | manual p.33B "INSTRUMENT SOURCE SELECTORS" |
| Main panel switches | L IRS | `B757_MAIN_L_IRS_BUTTON` | (0.83, 1.00, 20.72) | Captain instrument source selectors (Forward panel) | Captain instrument source selectors | manual p.33B "INSTRUMENT SOURCE SELECTORS"; manual p.96 "Captain INSTR SOURCE SEL" |
| Main panel switches | R IRS | `B757_MAIN_R_IRS_BUTTON` | (-0.83, 1.00, 20.72) | First officer instrument source selectors (Forward panel) | First officer instrument source selectors | manual p.33B "INSTRUMENT SOURCE SELECTORS" |
| Main panel switches | L Airdata | `B757_MAIN_L_AIRDATA_BUTTON` | (0.83, 0.97, 20.71) | Captain instrument source selectors (Forward panel) | Captain instrument source selectors | manual p.33B "INSTRUMENT SOURCE SELECTORS"; manual p.96 "Captain INSTR SOURCE SEL" |
| Main panel switches | R Airdata | `B757_MAIN_R_AIRDATA_BUTTON` | (-0.83, 0.97, 20.71) | First officer instrument source selectors (Forward panel) | First officer instrument source selectors | manual p.33B "INSTRUMENT SOURCE SELECTORS" |
| Main panel switches | Altn Gear Guard | `B757_MAIN_PANEL_ALTN_GEAR_SWITCH_GAURD` | (-0.19, 1.04, 20.73) | Landing gear (Forward panel) | Landing gear | manual p.33B "THRUST / GEAR / FLAP CONTROLS"; manual p.101 "alternate extension guarded" |
| Main panel switches | Altn Gear | `B757_MAIN_ALTN_GEAR_SWITCH` | (-0.19, 1.04, 20.73) | Landing gear (Forward panel) | Landing gear | manual p.33B "THRUST / GEAR / FLAP CONTROLS"; manual p.101 "alternate extension guarded" |
| Main panel switches | Alternate Flaps Selector | `B757_MAIN_knob_gears_altn_flaps` | (-0.14, 1.05, 20.73) | Alternate flaps (Forward panel) | Alternate flaps | manual p.33B "THRUST / GEAR / FLAP CONTROLS"; manual p.101 "ALTN FLAPS NORM, LE / TE OUT" |
| Main panel switches | L Leading Edge | `B757_MAIN_L_LEADINGEDGE_BUTTON` | (-0.13, 1.00, 20.72) | Alternate flaps (Forward panel) | Alternate flaps | manual p.33B "THRUST / GEAR / FLAP CONTROLS"; manual p.101 "ALTN FLAPS NORM, LE / TE OUT" |
| Main panel switches | L Trailing Edge | `B757_MAIN_L_TRAILINGEDGE_BUTTON` | (-0.15, 1.00, 20.72) | Alternate flaps (Forward panel) | Alternate flaps | manual p.33B "THRUST / GEAR / FLAP CONTROLS"; manual p.101 "ALTN FLAPS NORM, LE / TE OUT" |
| Main panel switches | Flap Override Guard | `B757_GAURD_8_SWITCH` | (-0.19, 0.99, 20.71) | Ground proximity overrides (Forward panel) | Ground proximity overrides | manual p.33B "GROUND PROXIMITY OVERRIDE SWITCHES" |
| Main panel switches | L Flap Ovrd | `B757_MAIN_L_FLAPOVRD_BUTTON` | (-0.19, 0.99, 20.72) | Ground proximity overrides (Forward panel) | Ground proximity overrides | manual p.33B "GROUND PROXIMITY OVERRIDE SWITCHES" |
| Main panel switches | Gear Override Guard | `B757_GAURD_9_SWITCH` | (-0.24, 0.99, 20.71) | Ground proximity overrides (Forward panel) | Ground proximity overrides | manual p.33B "GROUND PROXIMITY OVERRIDE SWITCHES" |
| Main panel switches | L Gear Ovrd | `B757_MAIN_L_GEAROVRD_BUTTON` | (-0.24, 0.99, 20.72) | Ground proximity overrides (Forward panel) | Ground proximity overrides | manual p.33B "GROUND PROXIMITY OVERRIDE SWITCHES" |
| Main panel switches | Terrain Override Guard | `B757_GAURD_10_SWITCH` | (-0.29, 0.99, 20.71) | Ground proximity overrides (Forward panel) | Ground proximity overrides | manual p.33B "GROUND PROXIMITY OVERRIDE SWITCHES" |
| Main panel switches | L Terr Ovrd | `B757_MAIN_L_TERROVRD_BUTTON` | (-0.29, 0.99, 20.72) | Ground proximity overrides (Forward panel) | Ground proximity overrides | manual p.33B "GROUND PROXIMITY OVERRIDE SWITCHES" |
| Main panel switches | L Brake Reserve | `B757_MAIN_L_BRAKERESERVE_BUTTON` | (0.31, 1.03, 20.73) | Brake controls (Forward panel) | Brakes | manual p.33B "BRAKE CONTROLS" |
| Main panel switches | CON | `B757_MAIN_CON_BUTTON` | (-0.12, 1.18, 20.78) | Thrust mode select (Forward panel) | Thrust mode select | manual p.33B "THRUST / GEAR / FLAP CONTROLS" |
| Captain chronometer | 757 FO Clock | `BBS_757_FO_CLOCK_BUTTON` | (-0.76, 1.38, 20.54) | First officer glareshield lighting panel (Glareshield) | First officer chronometer | manual p.33D "CLOCK SWITCH"; tooltip "First Officer Clock Start/Stop" |
| Cockpit miscellaneous | Cockpit Door | `B757_COCKPIT_DOOR` | (0.25, 1.64, 18.48) | Flight deck door (aft bulkhead) (Other) | Flight deck door | position (door leaf, 1.9 m tall) |
| Display brightness | Captain PFD | `B757_ADI_1_LCD_KNOB` | (0.18, 0.88, 20.47) | Captain EFIS control panel (Pedestal) | Captain EFIS | position; manual p.103 "Top: DH REF and ADI brightness. Center: ... HSI brightness" |
| Display brightness | First Officer PFD | `B757_ADI_2_LCD_KNOB` | (-0.24, 0.88, 20.47) | First officer EFIS control panel (Pedestal) | First officer EFIS | position; manual p.103 |
| Display brightness | Captain ND | `B757_HSI_1_SCREEN_KNOB` | (0.21, 0.88, 20.42) | Captain EFIS control panel (Pedestal) | Captain EFIS | position; manual p.103 "Top: DH REF and ADI brightness. Center: ... HSI brightness" |
| Display brightness | First Officer ND | `B757_HSI_2_SCREEN_KNOB` | (-0.21, 0.88, 20.42) | First officer EFIS control panel (Pedestal) | First officer EFIS | position; manual p.103 |
| Display brightness | Upper EICAS | `B757_EICAS_1_SCREEN_KNOB` | (-0.03, 0.95, 20.67) | EICAS control panel (Pedestal) | EICAS control | position; manual p.102 (DISPLAY ENGINE/STATUS, EVENT RECORD, COMPUTER, BRT, THRUST REF SET, MAX IND RESET) |
| Display brightness | Lower EICAS | `B757_EICAS_2_SCREEN_KNOB` | (-0.03, 0.94, 20.67) | EICAS control panel (Pedestal) | EICAS control | position; manual p.102 (DISPLAY ENGINE/STATUS, EVENT RECORD, COMPUTER, BRT, THRUST REF SET, MAX IND RESET) |
| Display brightness | Captain CDU | `B757_FMC_1_SCREEN_KNOB` | (0.14, 0.92, 20.60) | Captain CDU (Pedestal) | CDU brightness | position (inside CDU 1 face) |
| Display brightness | First Officer CDU | `B757_FMC_2_SCREEN_KNOB` | (-0.23, 0.92, 20.60) | First officer CDU (Pedestal) | CDU brightness | position (inside CDU 2 face) |
| Fire protection | Wheel Well Fire | `B757_PED_FIRE_WHLWELL_BUTTON` | (0.04, 0.85, 19.80) | Fire / overheat test (Pedestal) | Fire and overheat test | manual p.33E "FIRE / OVERHEAT TEST" |
| Fire protection | Fire ENG | `B757_PED_FIRE_ENG_BUTTON` | (0.01, 0.85, 19.80) | Fire / overheat test (Pedestal) | Fire and overheat test | manual p.33E "FIRE / OVERHEAT TEST" |
| Fire protection | Fire Cargo | `B757_PED_FIRE_CARGO_BUTTON` | (-0.01, 0.85, 19.80) | Fire / overheat test (Pedestal) | Fire and overheat test | manual p.33E "FIRE / OVERHEAT TEST" |
| Fire protection | Sys Fail | `B757_PED_SYS_FAIL_BUTTON` | (-0.04, 0.85, 19.81) | Fire / overheat test (Pedestal) | Fire and overheat test | manual p.33E "FIRE / OVERHEAT TEST" |

### Whole panels in the wrong section

| App section > panel | Rows | Rows physically elsewhere | Extent of its controls (m) |
|---|---|---|---|
| Overhead > Cabin lights | 4 | 4 on Other | x 1.07..1.42, y 1.99..2.03, z 16.94..16.94 |
| Overhead > Ground service | 1 | 1 on Other | x 1.39..1.39, y 1.99..1.99, z 16.94..16.94 |
| Forward panel > Captain EFIS | 12 | 12 on Pedestal | x 0.17..0.24, y 0.88..0.89, z 20.37..20.47 |
| Forward panel > Captain chronometer | 6 | 2 on Glareshield | x -0.76..0.76, y 0.97..1.38, z 20.54..20.73 |
| Forward panel > Oxygen | 12 | 12 on Other | x -0.92..1.36, y 0.90..1.54, z 19.14..20.64 |
| Forward panel > Cockpit miscellaneous | 1 | 1 on Other | x 0.25..0.25, y 1.64..1.64, z 18.48..18.48 |
| Forward panel > First officer EFIS | 12 | 12 on Pedestal | x -0.24..-0.17, y 0.88..0.89, z 20.37..20.47 |
| Forward panel > Display brightness | 8 | 8 on Pedestal | x -0.24..0.21, y 0.88..0.95, z 20.42..20.67 |
| Pedestal > Engines | 7 | 3 on Overhead | x -0.04..0.03, y 0.96..1.94, z 20.12..20.26 |

### Guards separated from their switches

| Guard (now in Switch guards) | Switch it covers | Distance | Switch's app panel now | Proposed (guard and switch) |
|---|---|---|---|---|
| Battery Guard `B757_GAURD_3_SWITCH` (0.18, 1.91, 20.19) | Battery `B757_OVHD_BATTERY_BUTTON` (0.18, 1.91, 20.19) | 0.2 cm | Electrical | Electrical |
| Elec Eng Cont 1 Guard (RR) `B757_GAURD_1_SWITCH` (0.32, 1.92, 20.16) | L ELEC ENG Cont (RR) `B757_OVHD_L_ELEC-ENG-CONT_BUTTON` (0.32, 1.92, 20.17) | 0.2 cm | Engine control and yaw damper | Electronic engine control (RR) |
| Elec Eng Cont 2 Guard (RR) `B757_GAURD_2_SWITCH` (0.27, 1.92, 20.16) | R ELEC ENG Cont (RR) `B757_OVHD_R_ELEC-ENG-CONT_BUTTON` (0.27, 1.92, 20.17) | 0.2 cm | Engine control and yaw damper | Electronic engine control (RR) |
| Passenger Oxygen Guard `B757_GAURD_6_SWITCH` (-0.04, 1.98, 20.03) | Passenger Oxygen `B757_OVHD_PASS_OXY_BUTTON` (-0.04, 1.98, 20.03) | 0.4 cm | Overhead miscellaneous | Emergency lights and passenger oxygen |
| Ram Air Turbine Guard `B757_GAURD_7_SWITCH` (0.04, 1.96, 20.08) | RAT `B757_OVHD_RAT_BUTTON` (0.04, 1.96, 20.08) | 0.1 cm | Overhead miscellaneous | Ram air turbine and engine limiters |
| Left Generator Drive Disconnect Guard `B757_GAURD_4_SWITCH` (0.17, 1.85, 20.34) | L Gen Drive `B757_OVHD_L_GEN_DRIVE_BUTTON` (0.17, 1.85, 20.34) | 0.3 cm | Electrical | Electrical |
| Right Generator Drive Disconnect Guard `B757_GAURD_5_SWITCH` (0.13, 1.85, 20.34) | R Gen Drive `B757_OVHD_R_GEN_DRIVE_BUTTON` (0.13, 1.85, 20.34) | 0.3 cm | Electrical | Electrical |

The other guards, already with their switches (verified position):

| Guard | Switch | Distance | App panel | Guard and switch |
|---|---|---|---|---|
| Emergency Lights Guard | Emergency Lights | 1.4 cm | Overhead miscellaneous | same panel |
| Altn Gear Guard | Altn Gear | 0.5 cm | Main panel switches | same panel |
| Flap Override Guard | L Flap Ovrd | 0.7 cm | Main panel switches | same panel |
| Gear Override Guard | L Gear Ovrd | 0.7 cm | Main panel switches | same panel |
| Terrain Override Guard | L Terr Ovrd | 0.7 cm | Main panel switches | same panel |
| 1 BTL Cargo Fire Guard | 1 BTL Cargo Fire | 0.2 cm | Fire protection | same panel |
| 2 BTL Cargo Fire Guard | 2 BTL Cargo Fire | 0.2 cm | Fire protection | same panel |
| Stab Trim C Guard | Stab Trim C Cutout | 0.6 cm | Stabilizer trim | same panel |
| Stab Trim R Guard | Stab Trim R Cutout | 0.7 cm | Stabilizer trim | same panel |
| P61 Glass Guard 1 | P61 Press Main Button L | 0.1 cm | P61 panel | same panel |
| P61 Glass Guard 2 | P61 Press Main Button C | 0.1 cm | P61 panel | same panel |
| P61 Glass Guard 3 | P61 Press Main Button R | 0.2 cm | P61 panel | same panel |

### App-made rows (not in the map)

| App row | Panel now | Switch node and position | Physically on | Proposed panel |
|---|---|---|---|---|
| Left/Right Rudder Trim 1 s; Rudder Trim readout (`B757_PED_RUDDER_TRIM_INDICATOR`) | Stabilizer trim | `B757_PEDESTAL_knob_rudder` (-0.03, 0.86, 19.87) | Aileron / rudder trim panel, aft aisle stand (verified manual p.33E, p.107) | Rudder and aileron trim (new, Pedestal) |
| Left/Right Aileron Trim 1 s; Aileron Trim readout (`B757_FC_YOKE`) | Stabilizer trim | `B757_Pedestal_switch_ailerons` (0.04, 0.86, 19.87) | Aileron / rudder trim panel | Rudder and aileron trim (new, Pedestal) |
| Nose Down/Nose Up Stab Trim 1 s | Stabilizer trim | `B757_Pedestal_switch_STAB_TRIM` (0.12, 0.96, 20.39) | Control stand, left side (verified manual p.104 picture "ALTN NOSE DN / NOSE UP") | Stabilizer trim (unchanged) |
| Climb/Descend Cabin 1 s; Outflow Valve readout | Pressurization | `B757_OVHD_CABINALT_SWITCH` (-0.15, 1.86, 20.30) | Pressurization (verified position, manual p.33A) | Pressurization (unchanged) |
| Landing altitude field and row | Pressurization | `B757_OVHD_LDG_ALT_KNOB` (-0.12, 1.83, 20.35) | Pressurization | Pressurization (unchanged) |
| 16 P61 test buttons | P61 panel | `B757_VC_P61_SWITCH_3..10` (-0.79, 1.96-2.01, 19.26-19.36) | P61 test panel | P61 panel (unchanged) |
| COM 1/2/3 standby fields and transfers | COM radios | the radio tuning panels' frequency knobs | VHF L/R/C radio tuning | COM radios (unchanged grouping) |

## Proposed structure

Rule used: one app panel per physical panel; adjacent plates of one system stay together when the manual's own flow
treats them as one item (Electrical, Air conditioning and bleed); a function split over distant places stays together
only where that is how a pilot uses it (Engines, the left/right pairs, the oxygen masks, Exterior lights, the clock
switch with its clock, the two CDU knobs); every guard sits with its switch, just before it. Panel order follows the
cockpit: the overhead in BlueBird's left-to-right flow order (manual p.39-40, p.80 "from left to right"), the forward
panel from the captain's outboard end to the first officer's, the pedestal from front to back. Row order inside a
panel is not proposed here (plan 12's every-day-first order would apply to the new panels).

Every one of the 409 map nodes is placed exactly once (checked by script: 409 nodes, 64 panels, no
duplicates, none left out). "all N rows of the current panel" means that panel moves whole, unchanged.

#### Overhead

| Proposed panel | Rows | Nodes (label `node`) | From current panel(s) | Change |
|---|---|---|---|---|
| IRS | 17 | IRS L `B757_IRS_L_SWITCH`; IRS C `B757_IRS_C_SWITCH`; IRS R `B757_IRS_R_SWITCH`; IRS Center Knob `B757_IRS_CENTRE_KNOB`; IRS Large Knob `B757_IRS_LARGE_KNOB`; IRS Number 1 `B757_IRS_NUMBER_1_BUTTON`; IRS Number 2 `B757_IRS_NUMBER_2_BUTTON`; IRS Number 3 `B757_IRS_NUMBER_3_BUTTON`; IRS Number 4 `B757_IRS_NUMBER_4_BUTTON`; IRS Number 5 `B757_IRS_NUMBER_5_BUTTON`; IRS Number 6 `B757_IRS_NUMBER_6_BUTTON`; IRS Number 7 `B757_IRS_NUMBER_7_BUTTON`; IRS Number 8 `B757_IRS_NUMBER_8_BUTTON`; IRS Number 9 `B757_IRS_NUMBER_9_BUTTON`; IRS Number 0 `B757_IRS_NUMBER_0_BUTTON`; IRS Number Ent `B757_IRS_NUMBER_ENT_BUTTON`; IRS Number Del `B757_IRS_NUMBER_DEL_BUTTON` | IRS (5); IRS keypad (12) | merge of IRS, IRS keypad |
| Electronic engine control (RR) | 4 | L ELEC ENG Cont (RR) `B757_OVHD_L_ELEC-ENG-CONT_BUTTON`; R ELEC ENG Cont (RR) `B757_OVHD_R_ELEC-ENG-CONT_BUTTON`; Elec Eng Cont 1 Guard (RR) `B757_GAURD_1_SWITCH`; Elec Eng Cont 2 Guard (RR) `B757_GAURD_2_SWITCH` | Engine control and yaw damper (2); Switch guards (2) | merge of Engine control and yaw damper, Switch guards |
| Yaw dampers | 2 | L Yaw Damper `B757_OVHD_L_YAW_DAMPER_BUTTON`; R Yaw Damper `B757_OVHD_R_YAW_DAMPER_BUTTON` | Engine control and yaw damper (2) | split from Engine control and yaw damper |
| Hydraulics | 6 | all 6 rows of the current panel | Hydraulics (6) | unchanged |
| Electrical | 15 | Battery `B757_OVHD_BATTERY_BUTTON`; EXT PWR `B757_OVHD_EXT_PWR_BUTTON`; STBY Power `B757_OVHD_STBY_POWER_SWITCH`; L Bus Tie `B757_OVHD_L_BUS_TIE_BUTTON`; R Bus Tie `B757_OVHD_R_BUS_TIE_BUTTON`; L Gen Cont `B757_OVHD_L_GEN_CONT_BUTTON`; R Gen Cont `B757_OVHD_R_GEN_CONT_BUTTON`; APU Gen `B757_OVHD_APU_GEN_BUTTON`; L Util Bus `B757_OVHD_L_UTIL_BUS_BUTTON`; R Util Bus `B757_OVHD_R_UTIL_BUS_BUTTON`; L Gen Drive `B757_OVHD_L_GEN_DRIVE_BUTTON`; R Gen Drive `B757_OVHD_R_GEN_DRIVE_BUTTON`; Battery Guard `B757_GAURD_3_SWITCH`; Left Generator Drive Disconnect Guard `B757_GAURD_4_SWITCH`; Right Generator Drive Disconnect Guard `B757_GAURD_5_SWITCH` | Electrical (12); Switch guards (3) | merge of Electrical, Switch guards |
| APU | 1 | all 1 rows of the current panel | APU (1) | unchanged |
| CVR | 2 | all 2 rows of the current panel | CVR (2) | unchanged |
| Panel lighting | 1 | Lt Ovrd `B757_OVHD_LT_OVRD_BUTTON` | Overhead miscellaneous (1) | split from Overhead miscellaneous |
| Emergency lights and passenger oxygen | 4 | Emergency Lights `B757_OVHD_PASS_OXY_Toggle_Switch_CLICK`; Emergency Lights Guard `B757_OVHD_SWITCH_GAURD`; Passenger Oxygen `B757_OVHD_PASS_OXY_BUTTON`; Passenger Oxygen Guard `B757_GAURD_6_SWITCH` | Overhead miscellaneous (3); Switch guards (1) | merge of Overhead miscellaneous, Switch guards |
| Ram air turbine and engine limiters | 4 | L ENG Limiter `B757_OVHD_L_ENG_LIMITER_BUTTON`; R ENG Limiter `B757_OVHD_R_ENG_LIMITER_BUTTON`; RAT `B757_OVHD_RAT_BUTTON`; Ram Air Turbine Guard `B757_GAURD_7_SWITCH` | Overhead miscellaneous (3); Switch guards (1) | merge of Overhead miscellaneous, Switch guards |
| Fuel | 8 | all 8 rows of the current panel | Fuel (8) | unchanged |
| Anti-ice | 3 | all 3 rows of the current panel | Anti-ice (3) | unchanged |
| Wipers | 1 | all 1 rows of the current panel | Wipers (1) | unchanged |
| Exterior lights | 10 | all 10 rows of the current panel | Exterior lights (10) | unchanged |
| Window heat | 4 | all 4 rows of the current panel | Window heat (4) | unchanged |
| SELCAL and cabin call | 11 | all 11 rows of the current panel | SELCAL and cabin call (11) | unchanged |
| Passenger signs | 2 | all 2 rows of the current panel | Passenger signs (2) | unchanged |
| Pressurization | 2 | Mode Select `B757_OVHD_MODE_SELECT_SWITCH`; Auto Rate `B757_OVHD_AUTORATE_SWITCH` | Pressurization (2) | keeps part of Pressurization |
| Equipment cooling | 1 | Equip `B757_OVHD_EQUIP_BUTTON` | Pressurization (1) | split from Pressurization |
| Air conditioning and bleed | 14 | all 14 rows of the current panel | Air conditioning and bleed (14) | unchanged |
| Indicator lights and door lock | 3 | Flight Deck Door `B757_OVHD_FLT-DK-DOOR_SWITCH`; Warning Test `B757_OVHD_WARNING_TEST_BUTTON`; Warning Ind `B757_OVHD_WARNING_IND_SWITCH` | Flight deck door (1); Warning lights test (2) | merge of Flight deck door, Warning lights test |

#### Glareshield

| Proposed panel | Rows | Nodes (label `node`) | From current panel(s) | Change |
|---|---|---|---|---|
| MCP | 23 | all 23 rows of the current panel | MCP (23) | unchanged |
| VOR/DME | 2 | all 2 rows of the current panel | VOR/DME (2) | unchanged |
| Master warning and caution | 2 | all 2 rows of the current panel | Master warning and caution (2) | unchanged |

#### Forward panel

| Proposed panel | Rows | Nodes (label `node`) | From current panel(s) | Change |
|---|---|---|---|---|
| Captain foot and shoulder heat | 2 | L Foot Heaters `B757_MAIN_L_FOOT_HEATERS_SWITCH`; L Shoulder Heaters `B757_MAIN_L_SHOULDER_HEATERS_SWITCH` | Main panel switches (2) | split from Main panel switches |
| Captain instrument source selectors | 5 | L Flt Dir `B757_MAIN_L_FLT_DIR_SWITCH`; L Flt Nav `B757_MAIN_L_FLT_NAV_SWITCH`; L EFI `B757_MAIN_L_EFI_BUTTON`; L IRS `B757_MAIN_L_IRS_BUTTON`; L Airdata `B757_MAIN_L_AIRDATA_BUTTON` | Main panel switches (5) | split from Main panel switches |
| Captain DDRMI | 2 | all 2 rows of the current panel | Captain DDRMI (2) | unchanged |
| Captain chronometer | 5 | 757 Capt Clock `BBS_757_CAPT_CLOCK_BUTTON`; Clock Button `INSTRUMENT_Knob_Clock`; Chrono ET `B757_MAIN_CHRONO_ET_SWITCH`; Chrono Set `B757_MAIN_CHRONO_SET_SWITCH`; Date Button `INSTRUMENT_Knob_Date` | Captain chronometer (5) | keeps part of Captain chronometer |
| Captain autoland status | 3 | all 3 rows of the current panel | Captain autoland status (3) | unchanged |
| Caution, config and G/S inhibit | 4 | Recall `B757_MAIN_RECALL_BUTTON`; Cancel `B757_MAIN_CANCEL_BUTTON`; Takeoff Check `B757_MAIN_TOCHK_BUTTON`; G/S Inhibit `BBS757_GS_INHIBIT` | Main panel switches (4) | split from Main panel switches |
| Standby instruments | 3 | Stdby EICAS `B757_MAIN_STDBY_EICAS_KNOB`; Gyro Cage Knob `BBS757_gyro_knob`; Gyro Selector `B757_MAIN_M_Gau_STBY_GYRO_SELECTOR` | Main panel switches (1); Standby attitude indicator (2) | merge of Main panel switches, Standby attitude indicator |
| Brakes | 2 | Autobrake `B757_MAIN_knob_gears_altn_autobrake`; L Brake Reserve `B757_MAIN_L_BRAKERESERVE_BUTTON` | Main panel switches (2) | split from Main panel switches |
| Heading reference | 1 | HDG Ref `B757_MAIN_HDG_REF_SWITCH` | Main panel switches (1) | split from Main panel switches |
| Thrust mode select | 6 | TOGA `B757_MAIN_TOGA_BUTTON`; TMSP 1 `B757_MAIN_1_BUTTON`; TMSP 2 `B757_MAIN_2_BUTTON`; CLB `B757_MAIN_CLB_BUTTON`; CRZ `B757_MAIN_CRZ_BUTTON`; CON `B757_MAIN_CON_BUTTON` | Main panel switches (6) | split from Main panel switches |
| Landing gear | 4 | Altn Gear Guard `B757_MAIN_PANEL_ALTN_GEAR_SWITCH_GAURD`; Altn Gear `B757_MAIN_ALTN_GEAR_SWITCH`; Gear Lever `B757_MAIN_Landing_Gear_Y`; Gear Lock Override `B757_MAIN_LOCK_OVRD_SWITCH` | Main panel switches (2); Landing gear (2) | merge of Main panel switches, Landing gear |
| Alternate flaps | 3 | Alternate Flaps Selector `B757_MAIN_knob_gears_altn_flaps`; L Leading Edge `B757_MAIN_L_LEADINGEDGE_BUTTON`; L Trailing Edge `B757_MAIN_L_TRAILINGEDGE_BUTTON` | Main panel switches (3) | split from Main panel switches |
| Ground proximity overrides | 6 | Flap Override Guard `B757_GAURD_8_SWITCH`; L Flap Ovrd `B757_MAIN_L_FLAPOVRD_BUTTON`; Gear Override Guard `B757_GAURD_9_SWITCH`; L Gear Ovrd `B757_MAIN_L_GEAROVRD_BUTTON`; Terrain Override Guard `B757_GAURD_10_SWITCH`; L Terr Ovrd `B757_MAIN_L_TERROVRD_BUTTON` | Main panel switches (6) | split from Main panel switches |
| First officer autoland status | 3 | all 3 rows of the current panel | First officer autoland status (3) | unchanged |
| First officer DDRMI | 2 | all 2 rows of the current panel | First officer DDRMI (2) | unchanged |
| First officer chronometer | 5 | 757 FO Clock `BBS_757_FO_CLOCK_BUTTON`; Clock Button `INSTRUMENT_Knob_Clock_FO`; Chrono ET `B757_MAIN_CHRONO_ET_SWITCH_FO`; Chrono Set `B757_MAIN_CHRONO_SET_SWITCH_FO`; Date Button `INSTRUMENT_Knob_Date_FO` | Captain chronometer (1); First officer chronometer (4) | merge of Captain chronometer, First officer chronometer |
| First officer instrument source selectors | 5 | R Flt Dir `B757_MAIN_R_FLT_DIR_SWITCH`; R Flt Nav `B757_MAIN_R_FLT_NAV_SWITCH`; R EFI `B757_MAIN_R_EFI_BUTTON`; R IRS `B757_MAIN_R_IRS_BUTTON`; R Airdata `B757_MAIN_R_AIRDATA_BUTTON` | Main panel switches (5) | split from Main panel switches |
| First officer foot and shoulder heat | 2 | R Foot Heaters `B757_MAIN_R_FOOT_HEATERS_SWITCH`; R Shoulder Heaters `B757_MAIN_R_SHOULDER_HEATERS_SWITCH` | Main panel switches (2) | split from Main panel switches |
| Yokes | 2 | all 2 rows of the current panel | Yokes (2) | unchanged |

#### Pedestal

| Proposed panel | Rows | Nodes (label `node`) | From current panel(s) | Change |
|---|---|---|---|---|
| Captain EFIS | 14 | ND Mode `B757_ADI_1_CTR_KNOB`; ND Range `B757_ADI_1_TFC_KNOB`; WXR `B757_ADI_1_WXR_BUTTON`; TERR `B757_ADI_1_TERR_BUTTON`; TFC `B757_ADI_1_TFC_BUTTON`; CTR `B757_ADI_1_CTR_BUTTON`; NAV AID `B757_ADI_1_NAV-AID_BUTTON`; APRT `B757_ADI_1_APRT_BUTTON`; DATA `B757_ADI_1_DATA_BUTTON`; WPT `B757_ADI_1_WPT_BUTTON`; DH Reset `B757_DH_RESET_CPT`; INOP `B757_ADI_1_INOP_BUTTON`; Captain PFD `B757_ADI_1_LCD_KNOB`; Captain ND `B757_HSI_1_SCREEN_KNOB` | Captain EFIS (12); Display brightness (2) | merge of Captain EFIS, Display brightness; section Forward panel -> Pedestal |
| EICAS control | 9 | Upper EICAS `B757_EICAS_1_SCREEN_KNOB`; Lower EICAS `B757_EICAS_2_SCREEN_KNOB`; EICAS R Status `B757_EICAS_R_STATUS_BUTTON`; EICAS L Status `B757_EICAS_L_STATUS_BUTTON`; EICAS Computer Switch `B757_ECAM_LRAUTO_KNOB`; EICAS Lwr Thrust Ref `B757_EICAS_LWR_THRUST-REF_KNOB`; Thrust Ref Pull `BBS757_THRREF_CLICK`; EICAS Max Ind `B757_EICAS_MAX-IND_BUTTON`; EICAS Event `B757_EICAS_EVENT_BUTTON` | Display brightness (2); EICAS control (7) | merge of Display brightness, EICAS control |
| First officer EFIS | 14 | ND Mode `B757_ADI_2_CTR_KNOB`; ND Range `B757_ADI_2_TFC_KNOB`; WXR `B757_ADI_2_WXR_BUTTON`; TERR `B757_ADI_2_TERR_BUTTON`; TFC `B757_ADI_2_TFC_BUTTON`; CTR `B757_ADI_2_CTR_BUTTON`; NAV AID `B757_ADI_2_NAV-AID_BUTTON`; APRT `B757_ADI_2_APRT_BUTTON`; DATA `B757_ADI_2_DATA_BUTTON`; WPT `B757_ADI_2_WPT_BUTTON`; DH Reset `B757_DH_RESET_FO`; INOP `B757_ADI_2_INOP_BUTTON`; First Officer PFD `B757_ADI_2_LCD_KNOB`; First Officer ND `B757_HSI_2_SCREEN_KNOB` | First officer EFIS (12); Display brightness (2) | merge of First officer EFIS, Display brightness; section Forward panel -> Pedestal |
| CDU brightness | 2 | Captain CDU `B757_FMC_1_SCREEN_KNOB`; First Officer CDU `B757_FMC_2_SCREEN_KNOB` | Display brightness (2) | split from Display brightness; section Forward panel -> Pedestal |
| Control stand | 7 | all 7 rows of the current panel | Control stand (7) | unchanged |
| Engines | 7 | all 7 rows of the current panel | Engines (7) | unchanged |
| Stabilizer trim | 4 | all 4 rows of the current panel | Stabilizer trim (4) | unchanged |
| Fire protection | 15 | Engine 1 Fire Handle Unlock `BBS757_FIREUNLOCK1`; Engine 1 Fire Handle `BBS757_FIREHANDLE1_CLICK`; Engine 1 Fire Handle Rotation `B757_PED_Fire_DISCH_1`; Engine 2 Fire Handle Unlock `BBS757_FIREUNLOCK2`; Engine 2 Fire Handle `BBS757_FIREHANDLE1_CLICK2`; Engine 2 Fire Handle Rotation `B757_PED_Fire_DISCH_2`; APU Fire Handle Unlock `B757_FIREUNLOCK3`; APU Fire Handle `BBS757_FIREHANDLE1_CLICK3`; APU Fire Handle Rotation `B757_PED_Fire_DISCH_3`; FWD Cargo Fire `B757_PED_FWD_CARGOFIRE_BUTTON`; AFT Cargo Fire `B757_PED_AFT_CARGOFIRE_BUTTON`; 1 BTL Cargo Fire Guard `B757_GAURD_11_SWITCH`; 1 BTL Cargo Fire `B757_PED_1BTL_CARGOFIRE_BUTTON`; 2 BTL Cargo Fire Guard `B757_GAURD_12_SWITCH`; 2 BTL Cargo Fire `B757_PED_2BTL_CARGOFIRE_BUTTON` | Fire protection (15) | keeps part of Fire protection |
| Radio tuning panel 1 | 9 | all 9 rows of the current panel | Radio tuning panel 1 (9) | unchanged |
| Weather radar | 11 | WX L WX `B757_PED_WX_L_WX_BUTTON`; WX L Wxt `B757_PED_WX_L_WXT_BUTTON`; WX L Map `B757_PED_WX_L_MAP_BUTTON`; WX L Gcs `B757_PED_WX_L_GCS_BUTTON`; WX L Test `B757_PED_WX_L_TEST_BUTTON`; WX L Tfr `B757_PED_WX_L_TFR_BUTTON`; WX R WX `B757_PED_WX_R_WX_BUTTON`; WX R Wxt `B757_PED_WX_R_WXT_BUTTON`; WX R Map `B757_PED_WX_R_MAP_BUTTON`; WX R Gcs `B757_PED_WX_R_GCS_BUTTON`; WX R Tfr `B757_PED_WX_R_TFR_BUTTON` | Weather radar left (6); Weather radar right (5) | merge of Weather radar left, Weather radar right |
| Radio tuning panel 2 | 9 | all 9 rows of the current panel | Radio tuning panel 2 (9) | unchanged |
| Audio panel 1 | 25 | all 25 rows of the current panel | Audio panel 1 (25) | unchanged |
| TCAS and transponder | 4 | all 4 rows of the current panel | TCAS and transponder (4) | unchanged |
| Audio panel 2 | 25 | all 25 rows of the current panel | Audio panel 2 (25) | unchanged |
| ILS tuning | 1 | ILS Test `B757_PED_ILS_TEST_BUTTON` | Pedestal radio panels (1) | rename of Pedestal radio panels |
| Radio tuning panel 3 | 9 | all 9 rows of the current panel | Radio tuning panel 3 (9) | unchanged |
| Fire and overheat test | 4 | Wheel Well Fire `B757_PED_FIRE_WHLWELL_BUTTON`; Fire ENG `B757_PED_FIRE_ENG_BUTTON`; Fire Cargo `B757_PED_FIRE_CARGO_BUTTON`; Sys Fail `B757_PED_SYS_FAIL_BUTTON` | Fire protection (4) | split from Fire protection |

#### Other

| Proposed panel | Rows | Nodes (label `node`) | From current panel(s) | Change |
|---|---|---|---|---|
| Oxygen | 12 | Captain Oxygen Test `OXY_CAPT_TEST_SWITCH001`; O2 Redbutton CPT `BBS757_O2_redbutton_CPT`; First Officer Oxygen Test `OXY_FO_TEST_SWITCH1`; O2 Redbutton FO `BBS757_O2_redbutton_FO`; Observer Oxygen Test `OXY_OBSV_TEST_SWITCH`; O2 Redbutton Jumpseat `BBS757_O2_redbutton_JUMPSEAT`; P61 Oxygen Test `OXY_P61_TEST_SWITCH`; O2 Redbutton P61 `BBS757_O2_redbutton_P61`; O2 Ears CPT `O2_EARS_CPT`; O2 Ears FO `O2_EARS_FO`; O2 Ears Jumpseat `O2_EARS_JUMPSEAT`; O2 Ears Observer `O2_EARS_OBSERVER` | Oxygen (12) | same rows; section Forward panel -> Other |
| P61 panel | 15 | all 15 rows of the current panel | P61 panel (15) | unchanged |
| Flight deck door | 1 | Cockpit Door `B757_COCKPIT_DOOR` | Cockpit miscellaneous (1) | rename of Cockpit miscellaneous; section Forward panel -> Other |
| Forward attendant panel | 5 | Cabin Ceiling `B757_CABIN_CEILING`; Cabin Window `B757_CABIN_WINDOW`; Cabin Entry `B757_CABIN_ENTRY`; Cabin Work Light `B757_CABIN_WORK_LT002`; GND Svc `B757_GND_SVC_BUTTON` | Cabin lights (4); Ground service (1) | merge of Cabin lights, Ground service; section Overhead -> Other |

App panels in the proposal: "COM radios" stays in Pedestal after TCAS and transponder; a new **Rudder and aileron
trim** (Pedestal, after Radio tuning panel 3) takes the four rudder/aileron trim buttons and their two readouts from
Stabilizer trim; Stabilizer trim keeps its four map rows and the two alternate stab trim buttons.

### What changes, in short

- **Split**: Main panel switches (into 10 panels; three rows join Landing gear and Standby instruments),
  Overhead miscellaneous (3), Switch guards (each guard to its switch), Display brightness (EFIS x2, EICAS control,
  CDU brightness), Engine control and yaw damper (Yaw dampers, Electronic engine control (RR)), Fire protection (Fire
  and overheat test), Pressurization (Equipment cooling), Stabilizer trim's app rows (Rudder and aileron trim).
- **Merge**: IRS + IRS keypad; Weather radar left + right; Flight deck door + Warning lights test (as "Indicator
  lights and door lock"); Cabin lights + Ground service (as "Forward attendant panel"); Standby attitude indicator +
  Stdby EICAS (as "Standby instruments").
- **Rename**: Pedestal radio panels -> ILS tuning; Cockpit miscellaneous -> Flight deck door; Standby attitude
  indicator -> Standby instruments. Optional renames to the manual's names: Radio tuning panel 1/2/3 -> VHF L/R/C
  radio tuning, Audio panel 1/2 -> Left/Right audio selector.
- **Move section**: Captain EFIS, First officer EFIS (Forward panel -> Pedestal); Oxygen, Cockpit door (Forward panel
  -> Other); Cabin lights, Ground service (Overhead -> Other).
- **Code that names panels by string** (would need the same change): `BBS757Definition.Readouts.cs`
  (`TransponderPanel`, `McpPanel`, `EnginesPanel`), `BBS757Definition.Efb.cs` (`GroundPowerPanel` "Electrical"),
  `Bbs757Annunciators.cs` (`ElectricalPanel`), `BBS757Definition.Overhead.cs` (`PressurizationPanel`),
  `Bbs757SpringControls.cs` (Stabilizer trim, Pressurization, P61 panel; the rudder and aileron controls' `Panel`
  becomes the new panel), `BBS757Definition.Radios.cs` (inserts COM radios after `TransponderPanel`). None of the
  panels those name is renamed in this proposal. Tests that pin panel names and the guide (plan 16) would follow.

## Label notes found on the way (not placement)

- `B757_EICAS_L_STATUS_BUTTON` "EICAS L Status" is the DISPLAY ENGINE button (its press writes
  `BBS_EICAS_ENGINE_PAGE_REQUEST`; it is the left one of the two (x 0.084 against 0.066), where the manual's picture has ENGINE: verified manual p.102 and position);
  `B757_EICAS_R_STATUS_BUTTON` "EICAS R Status" is DISPLAY STATUS (`BBS_EICAS_STATUS_PAGE_REQUEST`).
- `B757_IRS_LARGE_KNOB` "IRS Large Knob" is the upper (aft) knob, DSPL SEL; `B757_IRS_CENTRE_KNOB` "IRS Center Knob"
  the lower, SYS DSPL (verified position against manual p.33A).
- Oxygen: the first observer's box (left wall) is "Observer Oxygen Test", "O2 Redbutton Jumpseat", "O2 Ears
  Jumpseat" (`L:B757_OBS1_*`); the second observer's (right wall, under P61) is "P61 Oxygen Test", "O2 Redbutton
  P61", "O2 Ears Observer" (`L:B757_OBS2_*`). "Observer" names the left seat on one row and the right seat on
  another. Task 6 renames only the two test rows.
- The exterior light node names do not match their lights (`B757_OVHD_BEACON` is Position, `B757_OVHD_LOGO` Anti-col
  Red, `B757_OVHD_NAV` Anti-col White, `B757_OVHD_HI_INT` Wing), but the labels do: the left-to-right order on the
  plate matches the manual's POSITION / ANTI-COLLISION RED / WHITE / WING (verified position, manual p.33A).
- The P61 rows keep generic names ("P61 Press Button 1-6", "P61 Press Main Button L/C/R", "P61 Glass Guard 1-3"); the
  manual names the flight-control shutoff valves, PTU, recorder and service interphone on that panel (p.76).

## Open questions for the owner

1. Groupings kept that could be split to follow the cockpit strictly: Exterior lights (runway turnoff and logo are on
   the neighbouring plates), Electrical (battery and standby power is its own plate), Air conditioning and bleed (two
   plates). And the reverse: should Equipment cooling (one control) stay with Pressurization rather than stand alone?
2. Fire and overheat test: its own panel (as the cockpit) or kept with the fire handles (as a test of them)?
3. Display brightness was plan 12's own panel: distribute the knobs as proposed, or keep one "Display brightness"
   panel (then it belongs under Pedestal, where all eight knobs are)?
4. CDU brightness: the map has no CDU panel; keep the two knobs as a "CDU brightness" pair, or offer them in the CDU
   window?
5. G/S Inhibit sits with caution cancel/recall and T/O CHK in the centre panel's top left; put it there (proposed) or
   with the ground proximity overrides on the other side of the centre panel (same system)?
6. The glareshield CLOCK switches: with each pilot's chronometer (proposed) or as their own glareshield panels?
7. The flight deck door: the door itself alone under Other (proposed), or with its lock selector?
8. IRS: one 17-row panel (proposed) or keep the keypad as a second panel for a shorter Tab order?
9. Panel order: the cockpit order proposed here, or keep plan 12's ruling that panel order inside a section stays?
10. Section name "Other" for oxygen masks, P61, the door and the forward attendant panel: keep, or rename (for
    example "Cabin and side walls")?

## Files

- Per-node table (node, label, position, app panel, physical panel and evidence, proposed panel):
  `C:\Users\augus\AppData\Local\Temp\claude\D--Claude-oasis1701-msfs-blind-assist--claude-worktrees-branch-status-update-085d84\b94ca010-d557-42f4-9ff8-f626eb1a69a5\scratchpad\panel-audit\node-placement.tsv`
- Scripts: `C:\Users\augus\AppData\Local\Temp\claude\D--Claude-oasis1701-msfs-blind-assist--claude-worktrees-branch-status-update-085d84\b94ca010-d557-42f4-9ff8-f626eb1a69a5\scratchpad\panel-audit\gltf_pos.py` (world positions), `join.py`, `classify.py` (physical panel per node), `propose.py`
  (proposed panel per node), `render.py` (this report's tables); manual text and extracted pictures in the same
  folder (`*.txt`, `img\`).
