# Plan 12 ledger: "Airbus A350 Manual 1.5", PDF pages 1-30

One line per item: where a blind pilot reaches it in MSFSBA and the evidence that it works. Classes: **proven live** (date,
build), **built but not proven**, **gap** (with its owning plan), **not modelled** (with the aircraft's own file or symbol), and
**reference** (a ruling, plan 12: text a pilot reads and nothing to operate or watch: the manual's PDF has a text layer a screen
reader reads, and the aircraft shows nothing of it). "Panels table" is `docs/inibuilds-a350.md`'s per-panel sweeps (1.2.7,
2026-10-27 to 30); the 1.2.8 control map differs from 1.2.7's only in the oxygen masks (plan 11, Task 1), so those sweeps hold on
1.2.8 for every other control. Today's 1.2.8 re-sweeps: plan 11's evidence.

| PDF page | Item (the manual's words) | Where in MSFSBA | Class | Evidence | Owning plan |
| --- | --- | --- | --- | --- | --- |
| 2 | Preface: procedures written for single-pilot operation | the First Officer (Ctrl+Shift+F) takes the second pilot's flows and checklists | reference | `docs/first-officer.md` | 05 |
| 2 | Photosensitive seizure warning; copyright; version 1.5.0 | the manual | reference | | |
| 3 | Contents | the manual | reference | | |
| 4 | About: the A350-900, -900ULR, -1000 | one definition for all three (one module, `inibuilds-A350.wasm`) | built but not proven (-1000, -900ULR) | the -900 flown 2026-10-27 to 11-03; the others share the cockpit and module (`docs/inibuilds-a350.md` "Variants") | owner (loading a variant is the sim's menu) |
| 5-6 | Aircraft selection, cabin variant and livery (FREE FLIGHT, AIRCRAFT SELECTION, Configure) | the sim's own menus, outside MSFSBA | reference | | |
| 7 | Cockpit interaction: push, pull and scroll a knob (Lock or Legacy, Xbox) | each knob's push, pull, increase and decrease are rows of their own | proven live | Panels table: FCU knobs' push and pull, ECP SD knob, radar knobs (2026-11-03, 1.2.8, plan 11 sweep) | |
| 8 | Checklists in the native EFB | not reached: the sim's EFB tablet is outside the aircraft | gap | the same checklists are the MFD's C/L MENU (next line) and the First Officer's | owner |
| 8 | Checklists on the MFD via C/L MENU | MFD window: C/L MENU (Alt+K, "Go to MFD page"), each item a button | proven live | 2026-11-03, 1.2.8: ten checklists ticked (plan 09 Task 6) | |
| 9 | Recommended control sensitivity: linear, deadzones (Settings > Controls) | the sim's settings | reference | | |
| 10 | Weight limits per variant: MTOW, MLW, MZFW, OEW | the manual; the aircraft's weights read where it shows them (OIS LOADSHEET, MFD FUEL&LOAD, E/WD GW) | reference | OIS LOADSHEET (plan 06), FUEL&LOAD (plan 09 Task 6) | |
| 10 | Overweight landing permitted below MTOW with the procedure | the manual; the procedure on the E/WD when it applies | reference | | |
| 10 | Fuel capacity per variant | the manual; fuel on board on the E/WD and SD FUEL page | reference | E/WD FOB read (plan 04) | |
| 11 | VMCG, VMCA | the manual | reference | | |
| 11 | VLO, VLE (250 kt, M 0.55) | the manual; the PFD's VMAX band is drawn only | reference | the PFD reader reads VFE next, green dot, S and F (`A350Pfd`), not the VMAX band | |
| 11 | Max tire speed, max wiper speed, g limits, runway slope, max runway altitude | the manual | reference | | |
| 12 | VFE per CONF (1, 1+F, 2, 3, FULL) for each variant, max FL for slats and flaps FL200 | the manual; the PFD's VFE next is read | proven live (VFE next) | "VFE next 255 kt clean", "186 kt" on an approach (`A350Pfd`, live 2026-10-30) | |
| 12 | Slat, flap and aileron angles per CONF | the manual; the SD F/CTL page's flap and slat read | reference | | |
| 13 | Wind limits: tailwind (take-off, landing), crosswind, door operation wind | the manual; the wind is the ND's and the PFD's | reference | ND wind read (plan 08) | |
| 13 | Autoland limits: headwind, crosswind, tailwind, altitude, glide slope | the manual | reference | | |
| 14-16 | Aircraft configuration summary: each optional system and whether the OIS selects it (ADF, weathering, weight units, ANF auto zoom, auto derotate, AP disconnect protection, auto GPU disconnect, auto step climbs, auto remove covers, auto save, auto tiller disconnect, brake fans, custom thrust reduction altitude, display refresh rate, display type, EFB boot time, GPS jamming, IRS align time, light type, linked baro, panel state, pause at TOD, preferred seat side, printer, ROW/ROP, rudder tiller, SATCOM, sharklets, SND, T.O CONFIG requires CABIN READY, taxi camera light, time compression, VD in PLAN, wing hooks) | the OIS window, OPTIONS pages | built but not proven (each option on and off) | | 23 |
| 14-16 | Installed systems marked Yes or No (AUTO EMER DES, CPDLC, DDRMI, forward lavatory vent, HF data link, Li-ion and Ni-Cd batteries, No Mobile, No Smoking, rain repellent, SCS outlets, SLS, taxi camera, VD in PLAN No) | each system's own panel or display | reference (fitted or not) | the panels as fitted (Panels table) | |
| 17 | Specifications: cruise speed, max altitude, range, length, wingspan | the manual | reference | | |
| 18 | Single-pilot procedures; the flying management rules | the manual; the First Officer | reference | | 05 |
| 18 | The FMA at the top of the PFD: thrust, vertical, lateral, approach capability, AP/FD/A/THR columns | FMA call-outs (each change said, "FMA autothrust: ...") and the Displays section's Captain PFD | proven live | FMA call-outs flown 2026-10-31 and 11-03 (plan 07; spec 2.6) | |
| 19 | FMA colours: blue armed, green engaged, magenta constraints, white capability and AP/A/THR | each FMA cell said with its colour | proven live | "FMA autothrust: MAN, white; FLX, white; +67, cyan" (spec 2.6) | |
| 19 | 1FD2 on the ground; CLB and NAV armed | the FMA read | proven live | "[pfd] FMA engagement: 1 FD 2, white" (2026-11-03, plan 11 IRS run) | |
| 20 | A mode change boxed in white for 10 seconds | the FMA call-out says the change | built but not proven (the box itself) | the call-out says the new mode; whether the box is said is plan 07's open check | 07 |
| 20 | A mode reversion: flashing white box and a triple click | the new mode said; the triple click is the aircraft's sound | built but not proven | reversion (HDG pulled in CLB: OP CLB) to fly | 07 |
| 20 | Heading pulled: NAV to HDG and CLB to OP CLB | the FCU's HDG knob pull row; the FMA call-outs | proven live | HDG pulls flown 2026-10-30 to 11-03 (scripted circuits) | |
| 21 | Lights out: white lights off at set-up; blue temporary, amber caution, red warning | each panel's lights in its status display, each light said with what it shows | proven live | panel lights and their colours (Panels table; plan 11 sweep 2026-11-03) | |
| 21 | Amber lights before engine start are normal | the same | reference | | |
| 22 | Fly by wire: G-demand, auto trim, no pitch or bank hold | the aircraft's; the PFD's pitch and bank read | reference | | |
| 23 | 1 Loudspeaker knob | Loudspeakers and Table Lights: Captain loudspeaker (slider) | proven live | Panels table: "the knobs land on the value typed" | |
| 23 | 2 ATC message attention getter | Warnings: Captain ATC message; its light | proven live | plan 11 sweep 2026-11-03, 1.2.8 | |
| 23 | 3 Autoland warning light | Warnings: Captain autoland; "Autoland light" | proven live | plan 11 sweep | |
| 23 | 4 Chronometer | Warnings: Captain chrono (start, stop, reset); its time beside it; the ND's chrono | proven live | 2026-11-03, 1.2.8 (plan 11 Task 4) | |
| 23 | 5 Sidestick priority light | Warnings: Captain priority light and arrow light (shown, lit by the ANN LT test) | not modelled (lit only by ANN LT test) | the behaviour file only reads `INI_CPT_PRIORITY_UPPER_LIGHT`/`_LOWER_LIGHT` in the lamp rules, nothing writes them, the module never names them; the takeover key fires `INI.AUTOPILOT_OFF` (`A350Levers`) | |
| 23 | 6 CPT master warning and caution | Warnings: Captain master warning and caution; "Master warning" said | proven live | Panels table "every button"; plan 11 sweep | |
| 23 | 7 CPT EFIS | EFIS Captain panel | proven live | Panels table: every position and button | |
| 23 | 8 FCU | FCU panel, the FCU read-outs (Shift+H, S, A, V), typed fields | proven live | Panels table; flown | |
| 23 | 9 CPT pedal lights | Captain Side Panel: Captain pedal light | proven live | Panels table | |
| 23 | 10 CPT console light | Captain Side Panel: console light (slider) | proven live | Panels table | |
| 23 | 11 CPT map light toggles | Captain Side Panel: map light | proven live | Panels table | |
| 23 | 12 OIS view offside button | Captain Side Panel: OIS view offside and its lights | proven live | Panels table | |
| 23 | 13 OIS avionics/EFB toggle | Captain Side Panel: Captain OIS switch | proven live | Panels table | |
| 23 | 14 CPT outer DU | Captain Display Power; the PFD read (Alt+displays) | proven live | plan 11 sweep 2026-11-03; PFD read flown | |
| 23 | 15 CPT inner DU (ND) | the ND read, the ND window | proven live | plan 08 | |
| 23 | 16 FMS selector | Source Switching | proven live | Panels table: every position | |
| 23 | 17 Centre upper DU (E/WD) | Display Power: Center upper display; the E/WD read and call-outs | proven live | plan 04; plan 11 sweep | |
| 23 | 18 Gear indicator and autobrake panel | Gear, Autobrake panels; A/BRK; BTV | proven live | Panels table; BTV (plan 08) | |
| 23 | 19 Landing gear lever | Gear panel; the gear key | proven live | Panels table: every position | |
| 23 | 20-21 CPT footrest, tray table | Captain Table | proven live | Panels table: every row, 2026-10-30 | |
| 23 | 22 ISIS | ISIS panel; the SFD read (pitch, bank, speed, altitude, heading) | proven live | SFD read flown 2026-11-03 (plan 08 Task 4) | |
| 23 | 23-24 Centre lower DU and CPT outer DU; centre lower DU (MFD) | Display Power; the MFD window | proven live | plan 09; plan 11 sweep | |
| 23 | 25 Standby navigation display | SND panel; the SND read | proven live (read, MENU); built but not proven (MODE, +/-, LS/DIR TO, STD/CHANGE in flight) | plan 11 Task 2 | 11 |
| 23 | 26 Centre lower DU and FO outer DU toggles | Display Transfer: OIS on center, display cycle | proven live | plan 11 sweep 2026-11-03 | |
| 24 | 1, 3 CPT and FO KCCU | the MFD window (typing, KCCU keys) | proven live | plan 09 Task 5 | |
| 24 | 2 Thrust levers | Thrust Levers rows, lever call-outs | proven live | Panels table; flown | |
| 24 | 4, 6, 19 RMP 1, 2, 3 | the RMP window (Ctrl+Shift+R), the three RMP panels | proven live | plan 10, 2026-11-03, 1.2.8 | |
| 24 | 5 Engine masters and start selector | Engines panel | proven live | Panels table: each engine shut down and restarted | |
| 24 | 7 ECAM control panel | ECAM Control Panel (both sides and shared) | proven live | plan 11 sweep 2026-11-03 | |
| 24 | 8 Emergency descent button | Emergency Descent | proven live (ground) | Panels table: pressed on the ground | |
| 24 | 9 Speed brake lever | Speed Brake; spoiler call-outs | proven live | Panels table; flown | |
| 24 | 10 Surveillance panel | Transponder; the MFD SURV page | proven live | Panels table; plan 09 | |
| 24 | 11 Flaps lever | Flaps and Brakes; flap call-outs | proven live | Panels table; flown | |
| 24 | 12 Parking brake and accumulator indicator | Flaps and Brakes: parking brake; accumulator gauges as status lines | proven live | Panels table | |
| 24 | 13 Gravity gear extension | Flaps and Brakes: the gravity extension | proven live | Panels table: the whole procedure | |
| 24 | 14 Cockpit lighting | Cockpit: dome, flood, integral, DU master | proven live | plan 11 sweep | |
| 24 | 15 Pitch trim switches | Trim: pitch nudges; the pitch trim line and call-out | built but not proven (moving, heard) | plan 11 Task 4: trims do not move with the engines off | 11 |
| 24 | 16 Rudder trim | Trim: nudges and reset; the rudder trim line and call-out | built but not proven (moving, heard) | plan 11 Task 4 | 11 |
| 24 | 17 Cockpit door controls | Cockpit: door switch; Doors and Hatches | proven live | plan 11 sweep | |
| 24 | 18 Printer | Cockpit: printer paper; the paper status lines | proven live | Panels table | |
| 25 | 1, 22 CPT and FO windscreen wipers | Captain Wiper, First Officer Wiper | proven live | plan 11 sweep | |
| 25 | 2 Cabin call system and evacuation | Calls, Evacuation | proven live | Panels table | |
| 25 | 3 Crew and passenger oxygen supply | Oxygen | proven live | Panels table | |
| 25 | 4 Recorder ground control | Recorder and Misc: RCDR GND CTL | proven live | Panels table | |
| 25 | 5 ELT | Recorder and Misc: ELT, guard | proven live (switch); built but not proven (ELT EMITTING) | plan 11 Task 4 | 11 |
| 25 | 6 Emergency electrical power | ELEC | proven live | Panels table | |
| 25 | 7 Fuel jettison buttons | Fuel: jettison arm, active | proven live | Panels table (by hand, `keep`) | |
| 25 | 8, 26 Flight control panels | Flight Control Computers | proven live | Panels table | |
| 25 | 9 ADIRS switches | ADIRS | proven live | plan 11 Task 3: an alignment from OFF, 2026-11-03, 1.2.8 | |
| 25 | 10 Exterior lighting | Exterior Lighting | proven live | Panels table | |
| 25 | 11 APU master and start | APU | proven live | Panels table | |
| 25 | 12 Interior lighting and passenger signs | Interior Lighting, Signs | proven live | Panels table | |
| 25 | 13 Anti-ice | Anti Ice | proven live | Panels table | |
| 25 | 15 Air conditioning | Air Conditioning | proven live | Panels table | |
| 25 | 16 Electrical | ELEC | proven live | Panels table | |
| 25 | 17 Fuel | Fuel | proven live | Panels table | |
| 25 | 18, 27 Hydraulic, ground hydraulic | Hydraulics | proven live | Panels table | |
| 25 | 19 Engine and APU fire | Fire | proven live | Panels table: FIRE TEST | |
| 25 | 20 FADEC ground control | Engine FADEC and Manual Start | proven live | Panels table | |
| 25 | 21 Battery | ELEC | proven live | Panels table | |
| 25 | 23 Manual engine start | Engine FADEC and Manual Start | proven live | Panels table | |
| 25 | 24 Ventilation | Ventilation | proven live | Panels table | |
| 25 | 25 Cargo air conditioning | Cargo Air | proven live | Panels table | |
| 26 | HUD, deploy and stow, HUD controls, HUD cover | HUD Units (cover, combiner); HUD panel (brightness, crosswind, declutter); the HUD read | proven live | plan 11 sweep 2026-11-03; Panels table HUD | |
| 27 | Sun visors click spots | Sun Shades (each side, center) | proven live | plan 11 sweep 2026-11-03 | |
| 29 | Oxygen mask (captain's left side) | Captain Oxygen Mask | proven live | plan 11 Task 1, 2026-11-03, 1.2.8 | |
| 29 | Tiller and nose wheel steering disconnect | Captain Tiller and Sidestick | proven live | plan 11 sweep | |
| 29 | CPT sidestick | the sim's axes; Captain sidestick takeover | proven live | flown; plan 11 sweep | |
| 29 | Avionics bay access door; the jump seat retracted first | Doors and Hatches: avionics bay hatch; Observer Seat: jumpseat | proven live | Panels table: Doors and Hatches, Observer Seat | |
| 30 | Oxygen mask: 1 blinker, 2 test button, 3 Normal/100% selector, 4 overpressure selector | the mask's flow blinker light, mask test, N/100%, emergency pressure (and its push to test) | proven live | plan 11 Task 1, 2026-11-03, 1.2.8 | |
| 30 | Glareshield: 1 tray table light knob | Loudspeakers and Table Lights: sliding table light | proven live | Panels table | |
| 30 | 2 FCU display brightness knob | FCU: FCU brightness | proven live | Panels table | |
| 30 | 3 Glareshield integral lighting knob | Cockpit: integral light | proven live | Panels table | |
