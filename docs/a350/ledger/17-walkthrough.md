# Plan 17 ledger: the walk-through, "Airbus A350 Manual 1.5", PDF pages 87-123

The manual's walk-through (HAAB to FACT, SimBrief, ILS autoland) as steps in order, each with how a blind pilot does it in
MSFSBA. Task 1 lists them (2026-11-03, pages read with `pdftotext -layout`; pages 92, 102 and 105-107, whose pictures carry
the steps, read from their renders). Task 2, the owner's SimBrief flight, fills **Result** and **Evidence** as each step is
flown; until then **Result** says what earlier plans already proved through the same MSFSBA row or window, on the ground or
in the air, and "to fly" for the step itself.

Rows marked *(tutorials)* are steps recent online guides do that the walk-through leaves out
([flyawaysimulation: cold and dark](https://flyawaysimulation.com/ask/answers/start-inibuilds-a350-cold-dark-msfs/),
[flyawaysimulation: flying the A350](https://flyawaysimulation.com/ask/answers/fly-inibuilds-a350-msfs-2024/),
[flyawaysimulation: checklists](https://flyawaysimulation.com/ask/answers/use-inibuilds-a350-checklist-msfs/), read 2026-11-03),
or that the walk-through hands to "the in-sim checklist" (page 123).

Keys: OIS Shift+T (captain) and Ctrl+Shift+T (first officer); MFD window Shift+M; ND window Ctrl+Shift+B; RMP window
Ctrl+Shift+R; checklists (MFD C/L MENU) Ctrl+Shift+C; displays Alt+P, N, S, I, E; panels by section (Overhead, Glareshield,
Instrument, Pedestal, Cockpit).

| PDF page | Step | How the pilot does it in MSFSBA | Result | Evidence | Owning plan |
| --- | --- | --- | --- | --- | --- |
| 87 | Load at a stand at HAAB, cold and dark | the sim's world map (the pilot's own); OIS OPTIONS > AIRCRAFT > DEFAULT PANEL STATE COLD & DARK | to fly | plan 23 toggled DEFAULT PANEL STATE through the OIS window (2026-11-03) | 23 |
| 87 | *(tutorials)* Connect the GPU before external power | OIS GROUND > GPU (Shift+T) | proven live (ground) | plan 11, Panels "ELEC": EXT 1 and 2 AVAIL with the OIS GPU connected | |
| 87 | Batteries 1, 2, EMER 1, EMER 2 ON | Overhead > ELEC: BAT 1, BAT 2, BAT EMER 1, BAT EMER 2 | proven live (ground) | plan 11, Panels "ELEC" | |
| 87 | EXT 1 and EXT 2 ON | Overhead > ELEC: EXT 1, EXT 2 (state read: Available, On) | proven live (ground) | plan 11 | |
| 87 | ADIRS 1, 2, 3 to NAV | Overhead > ADIRS: ADIRS 1-3; Ctrl+3 status: IRS align and time remaining | proven live (ground) | plan 11; plan 11's IRS align status line (seconds) | |
| 88 | OIS OPTIONS > 3RD PARTY: SimBrief ID, Hoppie logon, Navigraph by QR code or link | OIS window, OPTIONS > 3RD PARTY (fields named by length only while STREAMER MODE is on, [A350-12]); Ctrl+V pastes | owner (credentials) | plan 06; plan 13 ledger | 06 |
| 88 | FLT OPS MENU > FLT OPS STS: SIMBRIEF IMPORT (or by hand), then LOADSHEET | OIS window: FLT OPS STS, SIMBRIEF IMPORT | to fly (needs the owner's SimBrief plan) | plan 06 (the page reads; the import pressed on the owner's account) | 06 |
| 89 | LOADSHEET: import ZFW and FUEL, SET ZFW, SET FUEL | OIS window: LOADSHEET, SET ZFW, SET FUEL | proven live (ground) | plan 06 | |
| 89 | TERML CHART: pick and pin charts | OIS window: TERML CHART (chart pictures: no text for a screen reader) | gap (pictures) | plan 13 ledger: chart pictures are the owner's question | 06 |
| 89 | GROUND page: doors as desired | OIS window: GROUND (doors, GPU, chocks) | proven live (ground) | plan 06 | |
| 90 | MFD DATA/STATUS: navigation database date; SWAP* to the second | MFD window (Shift+M): DATA/STATUS read | proven live (read) | plan 09 Task 1 (every page the menus reach) | |
| 90 | KCCU INIT; ACTIVE/INIT: import the flight plan from SimBrief | MFD window: INIT (Alt key), the INIT page's request and INSERT | to fly (SimBrief) | plan 09 (INIT typed and read back: "FROM: VCBI, cyan") | 09 |
| 91 | Crew oxygen supply ON (light out) | Overhead > Oxygen: crew supply | proven live (ground) | plan 11 (1.2.8 oxygen rename, `KEPT_KEYS`) | |
| 91 | RCDR GND CTL ON | Overhead > Recorder and Misc: RCDR GND CTL | proven live (the aircraft clears it at once) | plan 11, Panels "Recorder and Misc" | |
| 91 | ADIRS 1-3 NAV (again, in the flow) | as page 87 | proven live | plan 11 | |
| 91 | EXT LT: STROBE AUTO, BEACON OFF, NAV 1, LOGO AUTO, WING, LAND, NOSE OFF | Overhead > Exterior Lighting rows | proven live (ground) | plan 11 | |
| 91 | APU MASTER SW ON, then START | Overhead > APU: master switch, start (AVAIL read) | proven live (ground) | plan 11: AVAIL within a minute | |
| 91 | Signs: SEAT BELTS ON (after refuelling), NO SMOKING/PED AUTO, NO MOBILE AUTO, EMER EXIT LT ARM | Overhead > Signs | proven live (ground) | plan 11 | |
| 91 | APU BLEED ON once the APU runs | Overhead > Bleed Air: APU bleed | proven live (ground) | plan 11 | |
| 91 | Fuel pumps ON | Overhead > Fuel: six pumps | proven live (ground) | plan 11 | |
| 91 | *(tutorials)* Crossfeed normal, packs and hydraulics in auto | Overhead > Fuel, Air Conditioning, Hydraulics | proven live (ground) | plan 11 | |
| 93 | AIR DATA and FMS selectors AUTO | Instrument (or Pedestal) switching rows | proven live (ground) | plan 11 | |
| 93 | ISIS ON and aligned, QNH set | Instrument > ISIS; Ctrl+B altimeter window; Alt+I ISIS read | proven live (ground and air) | plan 02; Robin's KATL-KDTW flight (Altimeter window at FL200) | |
| 93 | Landing gear lever DOWN | Instrument > gear lever | proven live | plan 11 | |
| 93 | A/SKID and N/W STRG ON | Instrument > A/SKID & N/W STRG | proven live (ground) | plan 11 | |
| 94 | RMP set | RMP window (Ctrl+Shift+R) | proven live (ground) | plan 10 | |
| 94 | Spoilers RET | Pedestal > speed brake lever (`A350Levers`) | proven live | plan 11, "Pedestal levers" | |
| 94 | Parking brake ON | Pedestal > parking brake | proven live | plan 11 | |
| 94 | Engine masters OFF, ENG START NORM | Pedestal > engine masters, start selector | proven live (ground) | plan 11 (masters not swept, never in a test pass) | |
| 94 | Flap lever matches the PFD's flap position | Pedestal > Flaps; Alt+P PFD read | proven live | plan 11, plan 07 | |
| 95 | ACTIVE/INIT complete; IRS page: all three IRS in NAV; RETURN | MFD window: INIT, IRS, RETURN | proven live (read) | plan 09 Task 1 | |
| 95 | ACTIVE/INIT: DEPARTURE page | MFD window: DEPARTURE | proven live (read) | plan 09 | |
| 96 | DEPARTURE: runway, SID, transition; scroll with the KCCU arrows; TMPY F-PLN | MFD window: the lists (PageDown and PageUp a screen at a time), TMPY F-PLN | proven live (ground) | plan 09 Task 4 (lists paged) | |
| 96 | ACTIVE/F-PLN: INSERT TMPY | MFD window: F-PLN, INSERT TMPY; the F-PLN grid read | proven live | plan 09 (40-cell grid) | |
| 96 | *(tutorials)* Inspect every leg for discontinuities and odd turns | MFD window F-PLN grid ("right turn", "overfly", DISCONTINUITY rows) | proven live (read) | plan 09 Task 2 | |
| 96 | EFIS PLAN and range; CSTR on the ND to check constraints | Glareshield > EFIS mode, range, CSTR; ND window (Ctrl+Shift+B) | proven live | plan 08; plan 14 ledger | |
| 97 | Wind data from SimBrief; a secondary flight plan | MFD window: the wind request, SEC INDEX | to fly | plan 14 ledger (SEC pages read) | 09 |
| 97 | ACTIVE/INIT > FUEL & LOAD: ZFW, ZFWCG, BLOCK, PAX NBR | MFD window: FUEL & LOAD, typed | proven live | plan 09 | |
| 98 | Shortcut: click the field, KCCU ENT inserts the OIS value | MFD window: a field, Enter | to fly | | 09 |
| 98 | OIS T.O PERF: SYNC FMGS, SYNC, runway condition by hand, COMPUTE, SEND TO FMGS | OIS window: T.O PERF steps (typed as keys, [A350-6]) | proven live | plan 06; Robin's flight (V1 140, VR 141, V2 146, FLEX 67 matched COMPUTE) | |
| 98 | ACTIVE/INIT > T.O PERF: the OIS's figures inserted | MFD window: PERF T.O read | proven live | plan 09; Robin's flight | |
| 99 | FCU: QNH set | Glareshield > baro rows; Ctrl+B altimeter window | proven live | plan 02 | |
| 99 | EFIS as preferred | Glareshield > EFIS rows | proven live | plan 14 ledger | |
| 99 | SPD and lateral managed (dashes); push the knob if not | Glareshield > FCU knob push and pull rows (state read: Managed, Selected); Shift+S, Shift+H | proven live | plan 14 ledger | |
| 99 | Flight directors ON | Glareshield > FD | proven live | plan 14 | |
| 99 | Initial stop altitude on the FCU | Glareshield > FCU altitude (typed); Shift+A | proven live | plan 14 | |
| 100 | Cross-check PFD and ND: V1 and V2, CLB and NAV armed, FDs, stop altitude, altitude against elevation, QNH, SID name, first waypoint | Alt+P PFD read, Alt+N ND read, the FMA | proven live (read) | plan 07; plan 12 ledger | |
| 101 | OIS GROUND: CLOSE ALL; GPU and chocks removed | OIS window: GROUND | proven live | plan 06 | |
| 101 | Doors on the SD | Alt+S SD read (DOOR page) | proven live (read) | plan 11 (doors status lines) | |
| 101 | Pushback and start-up clearance from ATC | the pilot's ATC (SayIntentions, VATSIM, the ATC mailbox) | to fly | plan 22 (the mailbox) | 22 |
| 102 | APU AVAIL; APU BLEED ON; BEACON ON | Overhead > APU, Bleed Air, Exterior Lighting | proven live | plan 11 | |
| 102 | KCCU SURV; DEFAULT SETTINGS, CONFIRM | MFD window: SURV (Alt+V), DEFAULT SETTINGS, the confirmation's CONFIRM | proven live | plan 09 (the pop-up read) | |
| 103 | Transponder and TCAS active | MFD window SURV read; Ctrl+3 status | proven live (read) | plan 10, plan 15 ledger | |
| 103 | ENG START IGN/START; ENG MASTER ON; AVAIL on the EWD; then the other engine | Pedestal > start selector, engine masters; Alt+E E/WD read | proven live | Robin's flight; plan 04 | |
| 103 | *(tutorials)* Wait for a stable idle before the second engine | Alt+E E/WD (N1, N2, EGT) | proven live (read) | plan 04 | |
| 104 | ENG START NORM | Pedestal > start selector | proven live | plan 11 | |
| 104 | APU BLEED OFF, APU MASTER OFF | Overhead > Bleed Air, APU | proven live | plan 11 | |
| 104 | *(tutorials)* External power off once on the generators | Overhead > ELEC: EXT 1, EXT 2; OIS GROUND: GPU | proven live | plan 11 | |
| 104 | Ground spoilers ARM | Pedestal > speed brake lever: Armed | proven live | plan 11 | |
| 104 | Rudder trim RESET | Pedestal > rudder trim reset; the rudder trim call-out ("Rudder trim neutral") | proven live (ground) | plan 11 (`A350RudderTrimCallout`) | |
| 104 | FLAPS set for take-off (the pitch trim sets itself) | Pedestal > Flaps | proven live | plan 11 | |
| 105 | Flight control check: full deflection each way, the F/CTL page comes up by itself | the pilot's stick and pedals; Alt+S SD read (F/CTL) | to fly | | 11 |
| 105 | A/BRK armed (RTO) | Instrument > Autobrake: A/BRK | proven live | plan 08 ("BRK BTV" read back); plan 15 ledger | |
| 106 | WX on the EFIS panel | Glareshield > EFIS WX | proven live | plan 14 ledger (WXR knobs not modelled) | |
| 106 | Hold T.O CONFIG; no blue items in the take-off memo | ECAM control panel: T.O CONFIG; Alt+E E/WD read (memo, blue items) | proven live | plan 04; plan 16 ledger | |
| 106 | NOSE light TAXI | Overhead > Exterior Lighting: nose | proven live | plan 11 | |
| 107 | ANF: NAV or ARC, range into ZOOM; ANF on the ND | Glareshield > EFIS mode and range; ND window (Ctrl+Shift+B), the airport map | proven live | plan 08 | |
| 107 | Release the parking brake; taxi at idle below 30 kt, 10 kt in turns; brake in one smooth application | Pedestal > parking brake; taxi guidance; ground speed (Alt+N) | to fly | plan 08 (taxi on the ANF) | 08 |
| 107 | Parking brake on at the holding point | Pedestal > parking brake | to fly | | |
| 108 | STROBE ON, LDG ON, NOSE T.O | Overhead > Exterior Lighting | proven live | plan 11 | |
| 108 | PACKS as performance needs | Overhead > Air Conditioning: packs | proven live | plan 11 | |
| 109 | Line up; exit the ANF (range out of ZOOM) | Glareshield > EFIS range | to fly | | 08 |
| 109 | TOGA or FLEX; a FLEX temperature on the MFD PERF T.O page | MFD window: PERF T.O (FLEX); the thrust levers | proven live | Robin's flight (FLEX 67) | |
| 110 | Release the brake; levers to FLX (two detents) or TOGA (three); stick half forward; centreline on the rudder | Pedestal > Thrust levers (Flex/MCT, TOGA); the pilot's stick and rudder | to fly | plan 07 (take-off call-outs) | 07 |
| 111 | FMA at take-off: active and armed modes; V1 cyan 1, VR circle, V2 magenta | the FMA call-outs; Alt+P speed tape read | proven live | plan 07; `reference_a350_takeoff_phase_srs`: no SRS from idle | |
| 111 | Release the stick by 100 kt; V1; rotate at VR to 12.5 degrees at 3 per second | the take-off call-outs (V1, rotate); pitch read | to fly | plan 07 | 07 |
| 111 | Follow the FDs, AP1 on | Glareshield > AP1; FMA | proven live (air) | plan 07; scripted flights 2026-10-30 | |
| 111 | Positive climb: gear UP | Instrument > gear lever | proven live (air) | plan 11 (gear transit read is plan 11's flight item) | 11 |
| 112 | Acceleration altitude (OIS OPTIONS); LVR CLB flashing; levers to CL; CLB and NAV green | the FMA call-out ("LVR CLB"); Pedestal > Thrust levers: Climb | proven live (air) | plan 07; Robin's flight | |
| 112 | Above F speed: Flaps 1 (from Flaps 3 too) | Pedestal > Flaps; Shift+V characteristic speeds | proven live (air) | plan 07 | |
| 113 | Above S speed: Flaps 0 | Pedestal > Flaps | proven live (air) | plan 07 | |
| 113 | Disarm the speed brakes (push the lever in) | Pedestal > speed brake lever: Retracted | proven live | plan 11 | |
| 113 | NOSE lights OFF | Overhead > Exterior Lighting: nose | proven live | plan 11 | |
| 114 | Above 10,000 ft: landing lights OFF, seat belts AUTO | Overhead > Exterior Lighting, Signs | proven live | plan 11 | |
| 114 | Above the transition altitude: STD (push the baro knob) | Glareshield > baro push (STD); Ctrl+B altimeter window; the transition call-out | proven live (air) | plan 02; Robin's flight | |
| 115 | Cruise: monitor the systems | Alt+E, Alt+S, Alt+P, Ctrl+3 status | proven live | plans 04, 07, 11 | |
| 115 | Direct-To: DIRECT, DIRECT WITH ABEAM (CRS IN and OUT not used) | MFD window: DIR (Alt key), the waypoint's buttons, the abeam option | to fly | plan 09 (DIR without a flight plan is plan 09's flight item) | 09 |
| 116 | Direct-To from the ND: click the waypoint, Direct-To from the menu | ND window: the waypoint's menu | to fly | plan 08 | 08 |
| 117 | KCCU F-PLN; click the arrival airport; ARRIVAL: runway, approach, STAR, transition, VIA; INSERT TMPY; TMPY F-PLN | MFD window: F-PLN, the destination row's button, the ARRIVAL lists | to fly | plan 09 (lists, revision menus) | 09 |
| 118 | KCCU PERF > APPR: wind, temperature, QNH, minima; transition level, landing configuration, VAPP | MFD window: PERF, APPR tab, typed fields | to fly | plan 09; plan 14 ledger | 09 |
| 118 | ILS auto-tuned within 300 NM; KCCU RAD NAV: frequency checked | MFD window: RAD NAV (Ctrl+N); output N (the LS from the PFD) | proven live (ground) | plan 09; "Output N" (live 2026-10-30) | |
| 119 | Runway exit on the ANF; BTV armed | ND window: BTV group (Landing runway, BTV exit, Arm exit) | proven live | plan 08 Task 2 | |
| 119 | T/D: the white arrow on the path | ND read; the T/D pause (OIS SIMULATION) | proven live (air) | `reference_a350_takeoff_phase_srs`: the T/D pause needs phase past 1 | |
| 119 | Descent: FCU altitude 3,000 ft, push for managed descent | Glareshield > FCU altitude typed, altitude push; Shift+A | proven live (air) | scripted flights 2026-10-30 | |
| 120 | Preset the arrival QNH (turn, no push or pull); pull below the transition level | Glareshield > baro rows; Ctrl+B altimeter window | to fly (preset) | plan 02 (the preset is plan 02's) | 02 |
| 120 | Below 10,000 ft: landing lights ON, seat belts ON, LS ON | Overhead > Exterior Lighting, Signs; Glareshield > LS | proven live | plan 11, plan 14 | |
| 121 | PERF: ACTIVATE APPR*; or automatically past the D; the tab shows APPR in green | MFD window: PERF, ACTIVATE APPR | proven live (air) | scripted autoland 2026-10-30 (`reference_a350_scripted_flight`) | |
| 121 | APPR on the FCU on an intercept heading; AP2 | Glareshield > APPR, AP2 | proven live (air) | scripted autoland 2026-10-30 | |
| 122 | FMA: G/S and LOC blue (armed); ILS ident, frequency, DME at the PFD's lower left | the FMA call-outs; Alt+P PFD read (lower part) | to fly (PFD lower part) | plan 07; the PFD lower-part read is plan 12/14's flight item | 07 |
| 122 | VFE next (amber =), VAPP magenta, green dot, the magenta managed-speed dot | Shift+V characteristic speeds (VFE next, green dot, S, F); Alt+P speed tape | proven live | plan 07; parity list | |
| 122 | Flaps 1 below VFE next | Pedestal > Flaps | proven live (air) | scripted autoland | |
| 123 | After Flaps 2: gear DOWN; ground spoilers ARM; Flaps 3, FULL | Instrument > gear lever; Pedestal > speed brake, Flaps | proven live (air) | scripted autoland | |
| 123 | Landing memo below 2,000 ft: no blue items | Alt+E E/WD read | to fly | plan 04 | 04 |
| 123 | *(tutorials)* Confirm the FMA shows the landing capability for autoland | the FMA's capability call-out; approach capability key | proven live (air) | parity list (approach capability) | |
| 123 | Autoland; RETARD at 20 ft: levers to idle (or F1) | Pedestal > Thrust levers: Idle; the RETARD call-out | proven live (air) | scripted autoland 2026-10-30 | |
| 123 | Reverse after touchdown (F2 or the calibrated range); cancel at 60-70 kt (F1) | Pedestal > Thrust levers: Reverse, Reverse idle, Idle | to fly | plan 15 ledger (throttle reverse range is an OIS option) | 07 |
| 123 | AP off (side stick button); brakes (or A/THR disconnect) to take the braking back | Glareshield > AP off; brake keys; Thrust Levers: A/THR disconnect | to fly | plan 15 ledger (BTV deactivation) | 08 |
| 123 | Taxi to a stand | taxi guidance; GSX | to fly | | 08 |
| 123 | Shut down and secure with the in-sim checklist | Ctrl+Shift+C: C/L MENU (AFTER LANDING, PARKING, SECURING), each item a button | proven live (read) | plan 16 ledger; Robin's PARKING checklist at the gate | |
| 123 | *(tutorials)* After landing: spoilers disarmed, flaps up, strobe AUTO, landing lights off, APU start, WX off | Pedestal, Overhead, Glareshield rows | proven live | plan 11 | |
| 123 | *(tutorials)* Parking: engines off with the APU or GPU on, beacon off, fuel pumps off, park brake | Pedestal > engine masters; Overhead rows | to fly | Robin's flight (PARKING checklist) | |
| 123 | *(tutorials)* Securing: ADIRS OFF, oxygen off, APU off, batteries off | Overhead rows | proven live (ground) | plan 11 | |

## Summary

112 steps (9 from tutorials or the in-sim checklist). Proven through MSFSBA before the flight: 87. To fly: 23, all
in the owner's SimBrief flight (Task 2). Gaps so far: 1 (TERML CHART's charts are pictures). Owner: 1 (the credentials,
page 88), plus the SimBrief plan and the flight itself.
