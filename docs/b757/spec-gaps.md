# BlueBird 757-200: the gaps to a full flight (spec for plan set 02)

Investigation of 2026-10-25 (night), on the `B757` branch at `fbbeca097`, with the aircraft at KATL gate A1 on APU
power and the FMC programmed (KATL-KMIA). It answers the owner's list of gaps and asks what else an A-to-B flight
needs, using BlueBird's v1.01 manuals, the installed package, BlueBird's published variable list, the PMDG and iFly
profiles as the baseline, and the live sim. The plans that build it: `docs/b757/plans/02-gaps/` (start at
`00-index.md`). Like `docs/b757/spec.md`, this file argues from evidence; the integration plan deletes `docs/b757/`
before the branch merges.

**Sources**
- Live sim: `docs/b757/evidence/live-findings.md`, section "Gap investigation (session 8)". Every value below marked
  "measured" is from there.
- BlueBird's published variable list: <https://support.bluebirdsim.com/docs/simvars> (1,235 L:vars, "Values were
  checked against source as of 21 October 2026"). Cited as "BlueBird's list". Only the names and value meanings the
  plans use are copied into the plans; the page itself is not committed.
- BlueBird's manuals v1.01 (owner's copies, not committed): Complete Flight Guide (FG), Normal Checklist (NC), EFB
  Manual (EFB), with line numbers of their `pdftotext -layout` text.
- The package: `BBS_752_BASE_INTERIOR.xml` (cited "XML:line"), `BBS752.wasm` strings, the nine panel states.
- The app: PMDG 737/777 and iFly 737 MAX as the Boeing baseline (CLAUDE.md "Before changing behaviour": the iFly 737
  and PMDG 737 for systems, every aircraft for presentation).

## 1. The owner's list, answered

| # | Asked | What we found | Plan |
|---|---|---|---|
| 1 | Set the nav radios with input Ctrl+N | BlueBird's VOR and ILS receivers are its own: the sim's NAV 1-3 frequencies and courses never change (measured), so the shared Ctrl+N window (stock NAV events) would do nothing. VOR frequencies are tuned on the CDU's NAV RAD page (measured: typing 113.90 and pressing 1L tuned VOR L; DELETE returned it to auto). Courses are BlueBird variables that take a direct write (`BBS_VOR_L_COURSE`, `BBS_VOR_R_COURSE`, `BBS_ILS_COURSE`). The ILS frequency has no readable value; its knobs take steps only (§3.2). | 04 |
| 2 | Output D and Shift+D | Silent today. The CDU's PROG page 1 has the destination, its distance, ETA and fuel, and "TO T/C" (later "TO T/D") with time and distance (measured on the first officer's CDU), the iFly's source for the same keys. | 06 |
| 3 | COM changes are not announced | BlueBird's three VHF radios store their frequencies in the sim's own COM 1-3 variables (measured: VHF L is COM 1, VHF C is COM 2, VHF R is COM 3; the knobs step 8.33 kHz channels; a stock standby set sticks). So COM changes can be announced, and standby frequencies typed in, as on the PMDG 737. | 04 |
| 4 | "Position 1, Position 2" names | 35 controls still read "Position n". BlueBird's list names every one but two (table in §3.6). | 02 |
| 5 | Are the panel items wired correctly? | Mostly yes: a replay of all 424 controls' click code against their positions found every toggle, guard, select and switch step landing where it should, and live samples worked. The exceptions are listed in §3.7; the worst: the two stabilizer trim cutout switches have a broken write and their "On" means CUTOUT; the emergency lights switch is missing and its guard is labelled "Passenger Oxygen"; the hydraulic pump labels are wrong; 28 controls do not exist on the PW cockpit. | 02, 03 |
| 6 | No way to set pressurization cruise and landing altitude | Landing altitude: `L:B757_OVHD_LDG_ELEV` takes a direct write, the panel's digits follow after one knob step (measured). Cruise altitude: BlueBird's 757 has no FLT ALT selector at all (none in the cockpit or the module; the manual sets only landing elevation, FG 1612-1613, as on the classic 757 panel). The FMC's cruise level is set on the CDU (PERF INIT). | 03 |
| 7 | No emergency exit lights arm | The switch (`L:B757_OVHD_PASS_OXY_Toggle_Switch_CLICK`, 0 OFF, 1 ARMED, 2 ON in BlueBird's list) was left out because its state is not a plain variable and it only moves with its guard open; closing the guard forces ARMED (XML 1460-1550). | 02, 03 |
| 8 | No ALT intervene | The cockpit has an altitude-selector push (`BBS757_ALTSEL_CLICK`, the map's "ALT SEL Push"), but no module string reads the variable it writes and BlueBird's list says altitude intervention is not published. To prove in flight (M3); if it does nothing, the guide says to delete the constraint on the LEGS page. | 02, 05 |
| 9 | The SPD button does nothing; should it be speed intervene? No Mach. | It is the MCP SPD (autothrottle speed mode) button, and on the ground it does nothing (measured: no light, no change, A/T armed); in flight it engages A/T speed mode. Speed intervention is a different control, the IAS/MACH knob push, which the map calls "AP SPD Hold" (it writes `B757_MCP_IAS_MACH_KNOB_BUTTON`, BlueBird's "speed intervention command"). Mach: the flag `BBS_AIRSPEED_IN_MACH` and target `BBS_TARGET_MACH` exist; the changeover does nothing on the ground (measured), so Mach entry needs one airborne measurement (M1). | 05 |
| 10 | Keyboard shortcuts in the MCP windows | The windows already have Alt+letter keys (first letter of a button's last word: Alt+S SPD, Alt+I IAS/MACH…), and adding Speed Intervene would collide on I. Plan 05 gives every button a fixed key matching the PMDG windows (Alt+I is intervene in both). Owner question Q3 asks what kind of shortcut was meant. | 05 |
| 11 | EFB ground power connect and disconnect | The Ground Services page's GPU tile toggles the cart (measured: one tap disconnected it, a second reconnected it within 4 s; `L:BBS_757_GPU_AC_PWR_CONNECTED` follows). The owner's AUTOMATIC GPU/JETWAY setting connects it at load. The overhead EXT PWR button then puts it on line. | 07 |
| 12 | Flap speeds on Shift and the number row | No variable holds them, but the FMC's APPROACH REF page computes VREF 20/25/30 for any weight typed into it, so VREF30 was measured from 140,000 to 250,000 lb (§3.5). Maneuver speeds follow from VREF30. | 06 |

## 2. What else an A-to-B flight needs

From the Normal Checklist and the Flight Guide's flows (table in the manuals research, FG 1379-2612, NC 24-84),
against what the app does today and the PMDG 737 baseline. "Have" means the 757 profile already covers it.

| Phase | Needs | Today | Plan |
|---|---|---|---|
| Power up | Battery, APU, ground power, EXT PWR AVAIL/ON | Have panels; GPU and EXT PWR state not said | 07, 06 |
| Preflight | IRS NAV, align complete | IRS switches have; "aligned" not said; IRS display knobs "Position n" | 02, 06 |
| Preflight | Emergency lights ARMED | Missing | 02, 03 |
| Preflight | Pressurization: landing altitude, AUTO 1/2 | Landing altitude missing; Mode "Position n" | 02, 03 |
| Preflight | MCP: F/D, A/T ARM, bank limit, V2 in the speed window | Have; bank limit "Position n" | 02 |
| Preflight | Radios: VHF, ILS set or parked, transponder TA/RA | COM not settable or announced; ILS not settable; TCAS knob half named | 02, 04 |
| Preflight | FMC route, performance, takeoff data | Have (CDU window, ACARS) | - |
| Before start | Hydraulic electric pumps R, C, L | Have, but mislabelled (C1/C2 read as engine pumps) | 02 |
| Engine start | Start selector GND, VALVE light, N2, fuel control RUN, EGT | Have the switches and values; VALVE light not said | 06 |
| Taxi | Thrust for breakaway and taxi | Only with a hardware throttle: BlueBird ignores the sim's throttle keys; the levers take a direct write (measured) | 15 (optional, Q2) |
| Takeoff | THR, TO/GA, A/T armed, config warning | Have THR and TO/GA; A/T armed not said; config warning not said | 05, 06 |
| Climb | VNAV/FLCH, speed intervene, altitude intervene, Mach changeover | Intervene and Mach missing | 05, 02 |
| Cruise | Mach target, distance to destination and TOD, COM changes | Missing | 05, 06, 04 |
| Descent | Landing altitude, altimeters, minimums | Altimeters and DH have; landing altitude missing | 03 |
| Approach | ILS tuned and course set, LOC/APP, flap speeds, autoland LAND 3 / LAND 2 | ILS missing; flap speeds missing; autoland status not said (BlueBird publishes it: `B757_MAIN_L_AUTOLAND_1/2_BUTTON_ON`) | 04, 06 |
| Approach | Glideslope alive/captured | The shared "glideslope alive" call-out reads the sim's NAV 1, which BlueBird does not drive: it is wrong on the 757 | 01 turns it off; 13 looks for a source (M4) |
| Landing | Autobrake, speedbrake, reversers | Have autobrake and speedbrake; reversers by the sim's keys (unmeasured) | 15 (M9) |
| After landing / shutdown | Ground power, fuel control CUTOFF, emergency lights OFF | Covered by the above | 03, 07 |
| Throughout | Altitude alert, A/P and A/T disconnect, squawk and TCAS mode changes, doors | Only the CMD lights are said | 05, 06, 13 |

Out of scope for this set (follow-ups, each its own work): a First Officer and the Shift+C checklist (BlueBird's
Normal Checklist is short and would make a good checklist-only first version); ADF tuning; rudder and aileron trim;
weather radar tilt and gain; failures. The 757's own GPWS, stall and callout aurals are already heard and are not
repeated by the app.

## 3. Findings by area

### 3.1 COM radios (measured)
- `L:B757_VHF_L_FRAC_INC` +1 moved `COM STANDBY FREQUENCY:1` 120.900 → 120.905: 8.33 kHz channels, the module clears
  the request to 0. `B757_VHF_C_*` tunes COM 2, `B757_VHF_R_*` COM 3. The three tuning panels each select a radio
  (`BBS_RTP_L/C/R_LIGHTS`: bit 2 VHF L, 4 VHF C, 8 VHF R; read 2, 4, 8).
- `L:B757_PED_COMHEAD_1_SWAP_BUTTON` held 1 then 0 swapped COM 1. `K:COM2_STBY_RADIO_SET_HZ` 118275000 set COM 2
  standby, which stayed, and a knob step continued from it: the module keeps no frequency of its own.
- The PMDG 737, 777 and iFly announce "COM1 active 124.850" / "COM1 standby 121.900"; the FBW A380 says "VHF 1 active".

### 3.2 VOR and ILS (measured)
- The VOR/DME panel showed 116.90 AUTO while `NAV ACTIVE FREQUENCY:1..3` read 110.50; nothing done to BlueBird's
  radios changed any stock NAV variable. BlueBird's list publishes no frequency, only courses.
- VOR: `L:B757_MCP_VOR1_MODE_AUTO` / `VOR2` (1 auto); the VOR/DME button toggles it. In AUTO a frequency knob step
  does nothing. The CDU NAV RAD page shows `VOR L` / `VOR R` as `116.90AGSU` (A auto, R remote: tuned from the CDU,
  then the ident), `CRS`, `RADIAL`, `PRESELECT`; no ILS line. An entry in 1L/1R tunes; `DELETE` returns to auto.
  A CDU message covers the scratchpad and one CLR clears only the message (two earlier entries piled up behind one).
- ILS: `L:B757_ILS_FREQ_WHOLE_INC` / `FRAC_INC` (±1 requests) are consumed; with no readable frequency and the ILS
  panel outside every instrument view, how the knobs behave (PARK, wrap or stop at the ends) is unmeasured (M5, plan 04).
  APPROACH REF shows the FMC's runway ILS ("ILS 09L 110.50 IHZK, FRONT CRS 095").
- `L:BBS_ILS_COURSE` takes a direct write (95 held); `NAV OBS:1..3` stayed 0.

### 3.3 Pressurization and emergency lights (measured, BlueBird's list, XML)
- `L:B757_OVHD_LDG_ELEV` (feet, BlueBird's range -1,080 to 14,100): a direct write holds, the panel digits refresh
  on the next `L:B757_OVHD_LDG_ELEV_CHG` step (+1 = +10 ft). Exact set: write target − 10, then one CHG +1. No FLT ALT.
- Mode select: 0 AUTO 1, 1 AUTO 2, 2 MAN. Auto rate `L:B757_OVHD_AUTORATE_SWITCH` 0-13 in tenths, index 3.5.
- Emergency lights: `L:B757_OVHD_PASS_OXY_Toggle_Switch_CLICK` 0 OFF, 1 ARMED, 2 ON; guard `L:B757_OVHD_SWITCH_GAURD`
  0 closed, 1 open. Clicks work only with the guard open; closing it writes 1 (ARMED). Cold and dark: 0 with the
  guard open; every other panel state: 1, closed. Manual: ARMED, guard closed, before flight; OFF to terminate
  (FG 1565-1566, NC 82). Read now: 1, closed.
- Stab trim cutouts (`B757_Stab_trim_C/R_toggle_switch_CLICK`): 0 NORMAL, 1 CUTOUT, each behind a guard
  (`B757_AISLE_STAND_STAB_1/2_GAURD_SWITCH`) whose closing writes 0. Read now: 0, guards closed.

### 3.4 MCP (measured, BlueBird's list)
- On the ground the SPD button press and the IAS/MACH changeover both changed nothing. Speed intervention is
  `L:B757_MCP_IAS_MACH_KNOB_BUTTON` (written by the map's "AP SPD Hold", node `B757_MCP_AP_SPD_HOLD_SW`).
  `BBS_IAS_BLANKED` 1 means the speed window is blank (VNAV). `BBS_TARGET_MACH` read 0 in IAS mode.
- The speed window drums `B757_MCP_Gau_SPD_AUTO_100/10/1` read 20/10/10 at 200 kt (a digit d reads 10·d, 0 reads
  10): a fallback for reading Mach if `BBS_TARGET_MACH` does not track (M1, plan 05).
- Altitude selector push: `L:B757_MCP_ALT_SEL_PUSH`, read by the knob animation only (XML 12737-12752).
- `L:BBS_VNAV_TOD` read 429.24 with the route 568 NM: probably the distance to top of descent (M8, plan 06).

### 3.5 Speeds (measured)
VREF from the first officer's APPROACH REF with a trial GROSS WT (pounds × 1000; VREF 20 / 25 / 30, knots):

| GW | 140 | 150 | 160 | 170 | 180 | 190 | 200 | 210 | 220 | 230 | 240 | 250 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| VREF 20 | 119 | 124 | 128 | 132 | 136 | 140 | 144 | 148 | 151 | 155 | 158 | 162 |
| VREF 25 | 111 | 115 | 119 | 123 | 127 | 131 | 135 | 139 | 142 | 146 | 150 | 154 |
| VREF 30 | 109 | 113 | 117 | 121 | 125 | 129 | 133 | 137 | 140 | 144 | 148 | 152 |

The FMC's own 178.6 read 135/126/124. Below 140 and above 250 the FMC refuses the weight. Manual: flaps 5 maneuver
"approximately VREF 30 + 40", flaps up "VREF 30 + 80" (FG 6443-6444). Flap placard (cockpit, measured): 1 240,
5 220, 15 210, 20 195, 25 190, 30 162 kt. TAKEOFF REF showed V1/VR/V2 138/138/140, flaps 5, trim 4.00.

### 3.6 Position names (BlueBird's list)
IRS display Center knob: left, center, right IRS. IRS display Large knob: TK/GS, PPOS, WIND, HDG/STS. Wiper: OFF,
LOW, HIGH. Seatbelts, No Smoking: OFF, AUTO, ON. Mode Select: AUTO 1, AUTO 2, MAN. Pack selectors: OFF, AUTO,
STBY N, STBY C, STBY W. Cabin ceiling: Off, 33%, 66%, 100%. Cabin window and entry: Off, 50%, 100%. Warning
indicators: DIM (0), BRT (11). Bank limit: AUTO, 5, 10, 15, 20, 25. Alternate flaps: NORM, UP, 1, 5, 15, 20, 25, 30.
Standby attitude selector: OFF, ILS, BCRS. Chrono ET: RUN (0), HOLD (1), RESET (3), 2 unused. Chrono set: RUN, HOLD,
slow slew, fast slew. Ignition: 1, 2, BOTH. Engine start selectors: GND (1), AUTO (3), OFF (4), CONT (5), FLT (6).
TCAS mode: STBY, ALT RPTG OFF, XPNDR, TA ONLY, TA/RA. EICAS thrust reference: L, BOTH, R. EICAS computer: L, AUTO, R.
Stab trim cutouts: NORMAL, CUTOUT. P61 test switches: upper test, neutral, lower test (named per switch). Flight
recorder: ON, AUTO, TEST. Not named by anyone: the duplicate EICAS Computer knob (no 3-D node: dropped) and the
Duct Leak / Fuel Qty test switch (stays numbered).

### 3.7 Wiring audit (package replay of all 424 controls; live samples)
Clean: every other toggle, guard and select flips its own state var; every switch step moves one listed position;
no state var is shared; held buttons hold the base var. Live: Logo Lights and TCAS Mode replays worked and restored.
Defects, most serious first:
1. Stab Trim C and R: the press is not valid RPN (the extractor's gate note leaked in) and "On" is CUTOUT.
2. Emergency lights: switch missing, guard mislabelled "Passenger Oxygen Switch Guard".
3. Labels: "C Eng1/Eng2 HYD Pumps" are the C1/C2 electric pumps; "L/R ENG ELEC Pumps" are the L/R electric pumps;
   "Xfeed Pumps" are crossfeed valves; "Guard 11/12" unnamed; "Autopilot Disengage Bar" Off/On (1 = bar down);
   "Thrust Ref Pull" Off/On (0 = knob in); "AP SPD Hold" is speed intervene.
4. 28 map controls have no 3-D node on the PW cockpit (24 on the RR): the RR fuel switches on a PW, L/R ELEC ENG
   Cont, ADF switches, WX R Test, P6 display and event buttons, guards 13-15, the P61 panel guard, EICAS Computer.
5. Chrono ET: clockwise lowers the value, so the app steps it the wrong way.
6. Fire handle unlocks never relock after an app press (the relock runs on component `O:` vars the calculator
   cannot set); the APU fire handle has no state and a second press stows it.
7. EXT PWR says "pressed": its ON (`B757_OVHD_EXT_PWR_BUTTON_ON`) and AVAIL (`B757_OVHD_APUGENEXTPWR_R_AVAIL_DECAL`)
   lights are readable.
8. The 14 audio mic selects cannot reach "Off" (each press clears the others and sets itself).

### 3.8 Thrust levers (measured)
`L:BBS757_THRUST_LEVER_L` / `_R` (0 idle to 100 full) take a direct write: 20 held and drove the engine's throttle to
15.67 %. Session 7: BlueBird ignores the sim's throttle keys and events. Reverse is by the sim's reverser controls
only (BlueBird's list), unmeasured.

### 3.9 Things the app reads that are wrong on the 757
- Output N and the "glideslope alive / lost" call-out read stock NAV 1. Plan 01 turns the call-out off on the 757
  and plan 04 replaces N.

## 4. Decisions taken (each is the owner's to reverse)

Numbering continues plan 01's rulings.
- R51: Plan files follow the FenixV3 layout: one foundation plan lays seams, every other plan owns its files, shared
  docs go through the integration plan (owner, 2026-10-25: "no file conflicts").
- R52: Background CDU reads and writes (N, D, Shift+D, Ctrl+N's VOR frequencies) use the first officer's CDU, as the
  iFly reads PROG; the captain's CDU is never touched. The FO CDU is left on the page the app used. If wrong: a
  pilot using the FO CDU in the CDU window sees its page change.
- R53 (owner, Q1): COM words are the other Boeings': "COM1 active 124.850", "COM2 standby 121.500"; COM 1 is VHF L,
  COM 2 VHF C, COM 3 VHF R, which the guide says once.
- R54: A COM standby typed in is sent as the stock `COMn_STBY_RADIO_SET_HZ` (measured to stick and to be BlueBird's
  own storage); a transfer tries the stock `COMn_RADIO_SWAP` first and falls back to the tuning panel's transfer
  button whose panel selects that radio.
- R55: Ctrl+N's Enter applies only the field it is pressed in (the PMDG window applies all four): a VOR frequency
  goes through the CDU, so touching unchanged fields would cost seconds and FO CDU keys.
- R56: The shared glideslope call-out is off on the 757 until a BlueBird source is found (M4, plan 13); its Ctrl+M row
  goes too ([VAR-9]).
- R57: The MCP windows get fixed access keys: Heading S/H/L; Speed S SPD, T THR, F FLCH, M IAS/MACH, I Speed
  Intervene; Altitude H ALT HOLD, V VNAV, F FLCH, I Altitude Intervene (only if M3 proves it); V/S V. IAS/MACH moves
  from Alt+I to Alt+M. If wrong: a guide line.
- R58: While the speed window is blank (VNAV) the Ctrl+S box is read-only and says to press Speed Intervene first,
  as the PMDG 737 window does while the FMC owns the speed.
- R59: A typed Mach (0.78, .78, M.78, M0.78) or knots value that needs the other mode presses IAS/MACH first and
  waits for the flag; refused aloud when the flag does not follow ("IAS/MACH changeover is not available now").
- R60: MCP value changes from any source are announced ("MCP altitude 12000"), after the value rests 800 ms, never
  for the app's own set (echo) and never while the speed window is blank; each has a Ctrl+M row.
- R61 (owner, Q5: "standard flap table is good"): Flap maneuver speeds use BlueBird's measured VREF30 table and the
  757 schedule UP +80, 1 +60, 5 +40, 15 +20, 20 +20; Shift+1-5 are UP, 1, 5, 15, 20 as on the 777; Shift+6 reads
  VREF 30 and 25. Outside 140,000-250,000 lb: "Flap speeds unavailable."
- R62: The EFB's ground power buttons read `L:BBS_757_GPU_AC_PWR_CONNECTED` first and tap the toggle only when it
  differs (rule B757-2's pattern); disconnecting while EXT PWR is ON and no other source powers the buses is refused.
- R63: Landing altitude is a number field on the Pressurization panel (the squawk field's pattern), not a hotkey.
- R64: Emergency lights and the two stab trim cutouts become "guarded" controls: the app opens the guard, writes the
  position, and closes the guard for ARMED / NORMAL; OFF and ON leave it open, as BlueBird's panel states do.
- R65: Controls absent from the loaded engine variant's cockpit are left out of the map by the generator where they
  exist on neither model, and labelled "(PW)" / "(RR)" where they exist on one.
- R66: New automatic announcements, each with a Ctrl+M row and a silent first reading: autoland status (LAND 3,
  LAND 2, NO LAND 3, NO AUTOLAND), A/T armed, A/P and A/T disconnect lights, altitude alert, configuration warning,
  EXT PWR available / on, ground power connected, IRS aligned, start valves, doors, squawk and TCAS mode, COM.
  Not the GPWS, stall or callout lights: the aircraft voices them.
- R67 (owner, Q2): the owner flies with a hardware throttle, so plan 08 (thrust levers from the keyboard, for pilots
  without one) is optional and last.
- R68 (owner, 2026-10-25: "the earlier plans to be essentials and fixes and adding new items/features as later
  plans"): plan 01 is the foundation; plans 02-08 are fixes and essentials (fixes first inside each), so a full
  A-to-B flight is possible once they land; plans 09-15 are new features; plan 16 integrates. Plans of the same kind
  run in parallel and never share a file; a later plan takes a file over only after the early plan that owned it has
  landed.
- R69 (owner, Q4): no measurement plan. What needs a flight is measured on the owner's own flights during the work,
  by the plan that needs it, and recorded in that plan's own evidence file (`docs/b757/evidence/flight-NN.md`), so
  no two plans write one file.
- R70 (owner, Q6): planned now as later plans: a First Officer (10), as much EFB support as BlueBird allows with a
  reworked window (11), the rest of the panels and panel items with tests (12), cockpit-wide announcements (13), and
  (asked as its own plan) the CDU window's presentation for speech and braille (09).
- R71 (plan 09): a CDU label row made only of 20 or more dashes is dropped from the window (braille noise; the PMDG
  windows still show it).
- R72 (plan 09): CDU scratchpad messages are spoken while the CDU window is closed, as the FBW window does
  ([A320-34]), once each, with a check box in the window to turn it off.
- R73 (plan 06): the reworked EFB window uses the shared accessible EFB window the PMDG, MD-11 and flyPad EFBs use,
  fed by a 757 bridge built from readable state, if a spike shows it fits; otherwise WinForms tabs. It never shows
  BlueBird's screen (WASM-drawn, unreadable); it shows the app's model of what BlueBird's variables and settings say.
- R74: V-speed announcements are not planned: no variable holds them, and reading TAKEOFF REF on a timer would keep
  flipping the first officer's CDU; the pilot reads them in the CDU window.

## 5. Contracts between plans

Plan 01 creates these; later plans use them as written.
- Partial-method seams on `BBS757Definition` (`BBS757Definition.Hooks.cs`), one group per plan; table in plan 01.
- `Bbs757CduPages` (`SimConnect/BBS757/Bbs757CduPages.cs`): read either CDU, show a page, wipe the scratchpad, enter
  text into a line select key, over the existing tablet client.
- `Bbs757SharedKeys` (`Aircraft/BBS757/Bbs757SharedKeys.cs`): the variable keys more than one plan names:
  `ExtPwrOn = "BBS757_EXT_PWR_ON"`, `ExtPwrAvail = "BBS757_EXT_PWR_AVAIL"`, `GpuConnected = "BBS757_GPU_CONNECTED"`.
  Each key is registered by exactly one plan (06, 06, 07).
- `Bbs757Control` gains `Guard` (node of the guard control), `GuardClosed` (its closed value) and `CloseSets` (the
  value closing writes to the guarded control, or null); the generator (02) emits `guard`, `guard_closed`,
  `close_sets`.

## 6. Still to measure, on the owner's flights (R69)

Each item belongs to the plan that needs it (in brackets). M1 Mach changeover, `BBS_TARGET_MACH`, the knob's Mach step and tiers, the drums in Mach. M2 speed intervene in
VNAV (`BBS_IAS_BLANKED`). M3 the altitude selector push in a VNAV climb or descent with a constraint. M4 an L:var
hunt at each mode change (THR REF, VNAV, LOC and G/S capture, FLARE, ROLLOUT, A/P and A/T disconnect) for FMA and
glideslope sources. M5 the ILS panel: PARK, and whether the frequency knobs wrap or stop. M6 the SPD button in flight.
M7 the autoland status vars through an autoland. M8 `BBS_VNAV_TOD`. M9 reverse thrust with the sim's keys.
Owners: M1, M2, M6 [05]; M3 [02, then 05]; M4 [13]; M5 [04]; M7, M8 [06]; M9 [15]. The L:var snapshot tool for
them (plan 01 commits it) is described in `docs/b757/tools/lvar-snapshot/README.md`.

## 7. Toward PMDG parity

The owner first asked what full PMDG-level support would take, as scoping (2026-10-25), then chose (Q6, R70): the
First Officer, as much EFB support as BlueBird allows with a reworked window, the rest of the panels and panel items
with tests, cockpit-wide announcements and anything else that gives a full accessible experience, plus the CDU
window's presentation. Where each item now lives (all later plans): the CDU window → plan 09; First Officer → plan
10; EFB ground operations and the window rework → plan 11; weather radar, trims and the other dropped controls, and
the live sweep → plan 12; every light and switch announced → plan 13; Ctrl+P parity, squawk, TCAS mode and altimeter
changes and the standby AI read → plan 14. Still unplanned: ADF (nothing readable), V-speed announcements (R74), and
EICAS and FMA text (not published). Sizes are rough: S a day or less, M a few days, L a week or more, for one person.

| Item | What it would take | Size | Builds on |
|---|---|---|---|
| Every light and switch announced from any source (the PMDG 737 speaks about 400 rows) | A Coherent L:var watcher in the tablet agent (one call reads all 2,173 names in about 0.2 s, measured), so it costs none of the 900-variable budget; a curated table of the meaningful `_DECAL` lights out of 518 and the map's switch positions, with phrases; Ctrl+M grouping for hundreds of rows | L | plan 01's tablet client and agent |
| First Officer, checklist first | New files in `FirstOfficer/BBS757/` (profile, evaluator on `LVarStateEvaluator`, a do-nothing executor, read-only checklists from BlueBird's Normal Checklist with Captain reminders), one registration line in `FirstOfficerWindows`, three cross-profile tests, the b757 rule glob widened | M | the shared FO framework (MD-11 as the model) |
| First Officer actions and flows | An awaitable, silent write partial on the definition (the MD-11's pattern; the panel path is fire-and-forget), action groups, then flows and their cross-profile tests | L | the checklist-first FO |
| EFB ground operations | The other Ground Services tiles, whose states are readable (`BBS757_vis_stairsfwd`, `_stairsaft`, `_fueltruck`, `_catertruck`, `_aircon`, `_airstart`, `_baggage1`; jetway and tug through the sim), the CABIN & DOORS page, pushback by REQ PB TUG and the direction arrows; tile positions estimated from the session 8 mirror, each to be proven by a reversible tap | M | plan 06 |
| Squawk, TCAS mode and altimeter changes announced | The squawk and Ctrl+B set paths arming the echo window (files frozen in this set), then three rows | S | plans 01, 07 |
| V-speeds announced when set | TAKEOFF REF read through the first officer's CDU when the FMC's speeds change, or when the captain's CDU shows the page | S-M | plan 01's `Bbs757CduPages` |
| Standby instruments AI read (Alt+I) | One 757 prompt and one display-read row on the EICAS view, which frames the standby instruments (measured) | S | the display-read table |
| Weather radar tilt and gain, rudder and aileron trim | Direct writes and request variables the package audit found; trims through the sim's keys (BlueBird intercepts them) with a readout | S each | plan 02 |
| ADF | Tuning requests exist, but no frequency is readable | not worth it now | — |
| EICAS messages and FMA as text | Not published; AI reads only (Alt+E, Alt+P), unless plan 13 finds a source. A request to BlueBird for an export would close it | — | — |

## 8. Owner's answers (2026-10-25)

1. Q1, COM words: "like the other Boeings" (R53).
2. Q2, thrust: flies with a hardware throttle; plan 08 is optional (R67).
3. Q3, MCP windows: Alt+letter keys (R57, plan 05).
4. Q4, measurement flight: no separate plan; measured during the owner's own flights (R69).
5. Q5, flap speeds: the standard table (R61).
6. Q6, parity: the First Officer, the EFB with a reworked window, the rest of the panels with tests, cockpit-wide
   announcements, and a CDU presentation plan for speech and braille (R70). Order: fixes first (R68).
