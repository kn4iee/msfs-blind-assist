# iniBuilds A350 Airliner

> **Read this when:** working on the iniBuilds A350 (-900, -900 ULR, -1000): its panels, the WASM display reader, the KCCU/MFD, the ECAM, the OIS/EFB, or re-deriving addresses after an A350 update.

Measured live on 26 October 2026 against **A350 1.2.7** (package `inibuilds-aircraft-a350`, released 23 October 2026) in MSFS 2024, A350-900 powered at VCBI. It builds on the 3 October 2026 investigation of 1.2.6 (`development/a350-investigation`), whose method this aircraft depends on.

## Feasibility verdict

Everything the A350 shows or lets a pilot do is readable and operable without pictures, on all three variants (they share one module, `inibuilds-A350.wasm`):

| Surface | Read | Operate | How |
| --- | --- | --- | --- |
| Cockpit panels (696 controls) | yes | yes | L:vars named in each control's own set code; placard names from `en-US.locPak` (`INI.TOOLTIPS.*`) |
| FCU, both EFIS control panels | yes | yes | read: FCU gauge draw; operate: `INI_*_COMMAND` / `INI_FCU_*` L:vars |
| MFD, captain and first officer, every page | yes | yes | read: draw emulation with element rectangles; operate: Coherent `WASM_MOUSE_*` on the MFD gauge + KCCU key L:vars |
| PFD, ND, VD | yes | yes | read: EFIS draw; ND clicks (pop-ups, ANF, BTV exit) through Coherent `WASM_MOUSE_*` with the KCCU cursor on the ND |
| E/WD and SD (every SD page) | yes | yes | read: CTR draw, any SD page drawn on the copy; operate: ECP L:vars |
| ECAM procedures (sensed, ABN, DEFRD, STATUS) | yes | yes | procedure lines with colour; the ECP selection is the cyan `DrawWideFrame` |
| Failures | yes | yes | OIS FAILURES page (Coherent) |
| SFD and SND standby displays | yes | yes | ISIS-class draws; their buttons are input events |
| RMP 1-3 | yes | yes | RMP draw (frequencies come from cached CustomVariable values); keypad input events |
| OIS/EFB, both sides | yes | yes | ordinary HTML over Coherent (`iniEfbA350` views): pages, fields, selects, buttons |
| Cabin panel (FAP) | yes | yes | HTML over Coherent (`iniFAPA350`) |
| Printer | yes | n/a | HTML over Coherent (`A350Printer`); empty until something prints |
| BAT voltage display | yes | n/a | BAT draw; draws text only with the ANN LT switch in its normal position |
| HUD | when deployed | yes | HUD draw; stowed draws nothing |

Known limits, all shared with every other aircraft: terminal and en-route charts in the OIS are pictures; the tapes, attitude and map symbols on the PFD and ND are graphics, so those values come from variables, as on the FBW A380.

## The reader: running the A350's own draw code

Every A350 display except the OIS, FAP and printer is a WASM gauge drawn with NanoVG; the Coherent DOM of those views is empty (`WasmInstrument`). The reader:

1. **Copies the A350's linear memory**, read-only (`OpenProcess(QUERY_INFORMATION | VM_READ)`, `ReadProcessMemory`). Locate the allocation by its anchors (`INI_MCDU{}_ENTER`, `INI_MCDU_OPTION`, `INI_FMC_PAGE1` at their data-segment offsets inside the 4 GB+ reservations of `FlightSimulator2024.exe`), then list ONLY that allocation's regions (`VirtualQueryEx` from its base, about 10 regions) and read them back-to-back into one preallocated buffer: 37.7 MB in about 8 ms.
2. **Runs the A350's own `Draw()`** of the wanted gauge on that copy, in a WASM runtime, with the module patched so NanoVG reports what it draws.
3. **Rebuilds the screen** from the reported text, colour, transform and element records.

Nothing is written to the sim and the installed module is only read; the patched module and its compiled cache are built on the user's machine from their own copy and never shipped.

**Tearing.** A copy that spans A350 frames is inconsistent. Walking all 25,000 process regions while reading (0.5 s) gave empty or garbage WD lists in half the draws and once trapped `dlmalloc`. The 8 ms allocation-only copy gave 8 of 8 consistent draws. Still treat a trap as a torn copy and take another.

**Runtime.** Wasmtime for .NET (`Wasmtime` 48.0.2, net10.0): the 69 MB patched module compiles in 10 s with a 1.4 GB peak, once per A350 version; `Module.Serialize` caches it (118 MB), and the cached start takes 125 ms and 357 MB. Draws take 1-15 ms (CTR first draw 11 ms, then under 1 ms; MFD 2 ms).

### The patch (all found by name: the module keeps its full name section)

Each hook prepends a call to WASI `path_readlink` (an import never used while drawing) with a magic number as the sixth argument:

| Function | Magic | Reports |
| --- | --- | --- |
| `nvgText` | 0x7E57 | string, end, ctx, x, y |
| `nvgFillColor` | 0x7E58 | ctx, colour pointer (four f32) |
| `nvgSave` / `nvgRestore` | 0x7E59 / 0x7E5A | ctx (fill and transform stack) |
| `nvgTranslate`, `nvgScale`, `nvgRotate` | 0x7E5B-0x7E5D | ctx and the arguments |
| `nvgResetTransform`, `nvgReset`, `nvgTransform` | 0x7E5E-0x7E60 | ctx |
| `DrawFrame`, `DrawWideFrame` | 0x7E61 + 0x7E62 | rectangle, then colour (wide flag set): the ECAM selection box is a cyan wide frame |
| every `<Class>::draw(NVGcontext*, bool, int)` UI element (41 classes) | 0x7E65 | element pointer, ctx, class index: rectangle at element +4/+8/+12/+16 |

Exports added: `CTR::Draw`, `EFIS::Draw`, `FCU::Draw`, `HUD::Draw`, `ISIS::Draw`, `RMP::Draw`, `BAT::Draw`, `MFD::Draw`, `FMGS::Draw`, `SFD::Draw(int)`, `SND::Draw(int)`, `Systems::Draw`, `nvgBeginFrame`, `nvgEndFrame`. Texts must be placed with the tracked transform: the CTR draws the SD and WD with `nvgTranslate`, so raw positions overlap.

### Questions the draw code asks the sim

A draw makes a few sim calls, which the stubs must answer:

- **L:vars by id** (`fsVarsNamedVarGet`): most are `CustomVariable` objects on the heap, laid out `{u32 id, std::string name, f64 lastValue at +16}`. Scan the copy for that shape (1,357 found, 100 ms) and answer from the cached value: the copy is self-sufficient. `VariablesEnum` names use the id array at 1336752.
- **A: vars by name** (`get_aircraft_var_enum` / `get_units_enum` / `aircraft_varget`): the FCU and SFD ask `KOHLSMAN SETTING HG:1..3`, the EFIS and CTR `TURB ENG N2:1..2`. Supply them from SimConnect.
- **Calculator strings** (`execute_calculator_code`): the EFIS asks `(A:NAV IDENT:1..2, String)` and `(A:ADF IDENT:1..2, String)`.

Unanswered, those come back 0: QNH 0000 on the FCU, VHF 0.000 on the RMPs.

## Gauges and where they draw

| Gauge (Coherent view guid) | Object class | Contents |
| --- | --- | --- |
| EFIS ×2 (27 captain, 28 first officer) | `EFIS` | PFD left half, ND right half (x ≥ 836; the ND subtracts 836), VD, lower PFD (flaps, THS) |
| CTR (29) | `CTR` | left half: engine display (y < 300), SD page (300-940), SD permanent data (TAT, SAT, ISA, UTC, GW, CG, FOB); right half (x ≥ 822): the Warning Display |
| MFD (30) | `MFD` | captain FMGS at MFD+16 (x < 836), first officer FMGS at MFD+552 (drawn translated by 836) |
| ISIS ×2 (31, 32) | `ISIS` (`SFD`, `SND`) | standby flight display, standby navigation display |
| FCU (35) | `FCU` | SPD, HDG, ALT, V/S windows; both EFIS CP button labels and QNH |
| RMP ×3 (36-38) | `RMP` | VHF 1-3, data mode, squawk; the selected key draws highlighted |
| BAT (39), HUD ×2 (40, 41) | `BAT`, `HUD` | battery voltage; HUD symbology |

iniBuilds' ECAM class names are swapped: `EWD` draws the SD pages (`EWD::DrawLowerPage`) and `SD` draws the Warning Display (memos, procedures, STATUS limitations).

## Addresses for 1.2.7 (re-derive by name after an update)

| What | 1.2.7 | How to find it |
| --- | --- | --- |
| `Variables` f64 array | 1344512 | the `i32.const` in `Variables::GetInt` |
| `VariablesEnum` name table | 920832 (1939 names; `INI_FMC_PAGE1` = 303) | pointer array whose entries point at `INI_FMC_PAGE1` and its neighbours |
| `VariablesEnum` → L:var id | 1336752 | store in `Variables::Init` |
| SD page shown | i32 at 1451120 | `EWD::SetCurrentPage` |
| FMGS page vector | FMGS+52 (99 pages, the 1.2.6 order) | vector whose entries' vtables are `FMS_Status`, `FMS_Init`, `FMC_Ref`, … |
| FMS page shown flag | page+192 (u8) | the only byte set on the shown page alone |
| FMS page elements | page+196 / +200, 8-byte `(BaseUIElement*, int)` pairs | |
| MFD pointer position | FMGS+200 / +204 (f32) | `FMGS::Update` |
| pointer request slots | x 1082152, y 1082156, flags 1413577 (captain) / 1413578 (first officer) | `RequestMouseChange`; not needed (see below) |
| KCCU cursor screen | `INI_CPT_CURSOR_SCREEN` (1526) / `INI_FO_CURSOR_SCREEN` (1527): 1 ND, 2 MFD | |

The 1.2.6 anchors `INI_MCDU{}_TACT_MODE` and `INI_FMC{}_MCDU` no longer exist. 1.2.7 adds `INI_FMC{1,2}_KEY_INPUT` (the PC-keyboard capture mode of the manual's "click a field, then LEFT CTRL", read by `FMGS::HandleKeyInput`), `INI_FMC{1,2}_CLR_ENTRY`, and a crash-reporting consent dialog (`EULADialog`) on both MFDs at first start.

## Operating the displays: the sim's own mouse route

The gauge host views forward mouse input with `Coherent.call("WASM_MOUSE_MOVE" | "WASM_MOUSE_DOWN" | "WASM_MOUSE_UP" | "WASM_CLICK", guid, x, y, button)` (`wasm-sim-canvas`). Evaluated in that view over the debugger, these reach the A350: the gauge's `GaugeData` mouse position (+48/+56, f64) updates and the gauge's `MouseEvent` runs. The 1.2.6 finding that they "do not reach the A350 gauges" came from the cursor not being on that screen: the ND handles mouse input only while `INI_CPT_CURSOR_SCREEN` is 1, the MFD while it is 2. Move the cursor with the KCCU keys `INI_MCDU{1,2}_ND` / `_MFD`.

Verified live: a move put the captain MFD pointer exactly where sent; a move-down-up-click on ACCEPT dismissed the crash-report dialog; clicking the FROM box focused it; `INI_MCDU1_V`, `_C`, `_B`, `_I`, `_ENTER` entered VCBI, and TO WMKK made DEPARTURE available and CRZ FL a required (amber) box; a click on the aircraft symbol opened the ND pop-up (P.POS, DIR TO), and a click on empty map closed it; the first officer's MFD takes x + 836. No write into the sim's memory is needed, so the investigation's pointer request is not used.

## ECAM control panel

The ECP buttons are L:vars the aircraft resets itself after reading. Press with ONE native SimConnect write of 1; two calculator writes (1 then 0) land in the same frame and do nothing. Verified: `INI_ECAM_CHECKLIST_LEFT` (check-off), `PUSH_ECAM_STS`, `INI_ECAM_ABN`, `INI_ECAM_WHEEL_L_MINUS_COMMAND` (selection down; PLUS is up), `INI_MASTER_CAUTION_COMMAND`. The other ECP keys follow the same pattern in their input events' set code.

Failure test: OIS FAILURES → PRIM 1 FAULT gave the amber `F/CTL PRIM 1 FAULT`, the cyan `PRIM 1 ....OFF THEN ON`, `PRIM RESET SUCCESSFUL ?` with `YES` / `NO...`, `CLEAR`, the SD switched to F/CTL; the check-off completed it into STATUS (INFO, `APPR 1 ONLY`, `LAND 3 SINGLE ONLY`, INOP SYS `PRIM 1` in amber, ALERTS IMPACTING LDG PERF) and STS cleared it. ABN listed every not-sensed procedure with its colour.

## Panels

`A350_Interior.behavior.xml` holds 696 input events (RMP 192, FCU 44, EFIS 38, ECAM 32, DISPLAY 30, ELEC 26, WXR 20, MAINT 17, OXYTEST 16, AIR/FUEL/ISIS 14, FCTL 13, …). It parses with the iniBuilds `behavior_xml.py` after rewriting tags that begin with a digit. Each event's set code writes `INI_*` L:vars directly (for example `INI_SEATBELTS_SWITCH`, `INI_BATTERY_1_SWITCH`, `INI_MIXTURE_RATIO1_HANDLE` for ENG MASTER 1), so a native L:var write operates the control; live: `INI_NO_SMOKING_SWITCH` 1 → 2 changed the takeoff memo's SIGNS line from done to outstanding, and back. Position names come from each control's own tooltip expression: some count from `.1` (`SIGNS_SEAT_BELTS.1` = ON is value 0), others from `.0` (`MAINT_ELEC_BAT_VOLTAGE_SELECTOR.0` = EMER 1).

MSFSBA writes a row with its own input event, `value (>B:AIRLINER_<id>_Set)`, on the calculator path (`A350WritePlan`). `tools/A350Probe` drives rows through MSFSBA's own definition and SimConnect path (`live <panel> [watched L:vars]` tries every position, reads back, restores, and logs the panel's lights and the watched variables that changed); close MSFSBA first. Probing the same path with the simconnect MCP's `execute_calculator_code`: each string lands only when the NEXT calculator string arrives, so a read straight after a write shows the previous write. Follow every probe write with a harmless unique string (`n (>L:MSFSBA_CALC_TEST)`) before reading back.

Only the Overhead section is offered so far; a section joins once each of its panels has been driven live (`IniBuildsA350Definition.CuratedSections`). The panels' lights come from the map's emissive rules (`A350PanelLamps`): a lamp belongs to the row whose id is the longest prefix of its node, reads On only while its rule holds and its bus's light power (`INI_AC_LIGHTS_FAILURE` / `INI_DC_LIGHTS_FAILURE`, 1 = powered) is on, and a legend that reads nothing but its own switch is left out.

### Overhead subpanels, live-tested (A350-900, 1.2.7)

| Panel | Rows | Verified | Notes |
| --- | --- | --- | --- |
| Wipers | captain and first officer wiper (6 positions), both rain repellents (hold) | every position of both selectors; the wipers sweep (`WIPER_ANIMATION_CPT`/`_FO`); each repellent holds 1 while pressed | the repellent variable is read only by the cockpit sound: a press is a spray sound and nothing else, for a sighted pilot too |
| Calls and Evacuation | nine call buttons, EMER, EVAC command, EVAC horn off, EVAC CAPT and PURS; lights EMER call, EVAC | every row through MSFSBA's path; EVAC command lights the EVAC legend (`INI_EVAC_LIGHT`) within seconds and puts it out when off | the systems read only FWD, PURS and the EVAC variables; the other calls are chimes. EMER CALL lights only on a cabin call. Each guarded button's set code ignores its guard |
| Oxygen | crew supply, mask man on | both rows, both positions | MASK MAN ON drops the cabin masks; its set code ignores its guard |
| Recorder | ACMS trigger, DFDR event, RCDR GND CTL, CVR test, CVR erase | the four buttons press | RCDR GND CTL flips and the aircraft clears it at once: `GENERAL_SYSTEMS::Update` sets `INI_GND_CTL` to 0 with an engine at N2 52 % or more or off the ground (`AcfVariables` 3, SIM ON GROUND), and it did so on the ground with both engines stopped. A click runs the same set code, so this is the aircraft |
| ELT | ELT (TEST/RESET, ARMED, ON), ELT guard | every position | TEST/RESET is taken only with the guard open (`INI_ELT_SWITCH_GUARD` 1); otherwise the switch springs back. The guard's tooltip words are the click's action (OPEN at 0) |
| Emergency Electrical Power | RAT MAN ON, EMER GEN; EMER GEN fault light | EMER GEN both positions | RAT MAN ON is not pressed in a test pass: it deploys the RAT |
| Fuel Jettison | fuel jettison arm, fuel jettison active; their lights; the option line | with the option fitted, both rows and the valve (OPEN legend) | an equipment option (`INI_OPTION_FUEL_JETTISON`, OIS options page): without it the cockpit hides the panel and `UpdateFuelJettison` clears all three variables every frame, so MSFSBA refuses a press aloud and the panel's status line says "Not fitted" (`A350Options`) |
| Flight Controls | PRIM 1-3, SEC 1-3; their fault lights | each off and back on | |
| ADIRS | ADIRS 1-3 (OFF, NAV, ATT), IR 1-3, ADR 1-3; their fault and OFF lights | every IR and ADR; ADIRS 3 through every position; ADIRS 1 and 2 to ATT and back | an IR off drops its IRS's alignment flag (`INI_IRSn_ALIGNED`) until it is back on; ADIRS OFF loses the alignment, which restarts at NAV. The ADR 2 and 3 FAULT lamps read `INI_ADF2_FAULT` / `INI_ADF3_FAULT` (iniBuilds' spelling) |
| Exterior Lighting | beacon, landing, logo, nav, nose, strobe, wing | every position | beacon and wing follow on the sim's own light state; the nav lights are drawn from `INI_LIGHTS_NAV` by the model, so the stock LIGHT NAV stays on whatever the switch |
| APU | master switch, start; master fault, start AVAIL and ON lights | master on, start: the APU ran up and AVAIL lit within a minute | |
| Interior Lighting and Signs | annunciator light, standby compass light, EMER exit light, no mobile, no smoking and no PED, seat belts, three occupant light knobs, coat stowage light | every position; the knobs land on the value typed | the signs reach `INI_NO_SMOKING_ON` / `INI_SEATBELTS_ON` |
| Anti-Ice | wing, ENG 1, ENG 2, probe and window heat; three fault lights | each on and off | |
| Air Conditioning | crossbleed, windshield defog, cockpit and cabin temperature knobs, ram air, packs, hot air, air flow, ENG and APU bleeds; their fault lights | every position | the temperature knobs (0-100) are written by the systems module only through the panel-state presets; what they select is on the SD COND page |
| Cabin Pressure | cabin altitude and V/S targets (encoders) and modes, ditching; the two targets as status lines | modes to MAN and back; each target steps 100 ft or 100 ft/min a click in MAN | an encoder's value is a status line on its panel (`A350Readouts`) |
| Electrical | BAT 1/2, BAT EMER 1/2, bus tie, commercial 1/2, ELM, EXT 1/2, galley, GEN 1A-2B, APU GEN, DRIVE 1A-2B, PAX SYS, side 1 and 2 isolation; 23 lights | every switch off and on; with the OIS GPU connected (GROUND, GPU), EXT 1 and 2 each on line and off | a battery's FAULT legend lights a moment after it goes off. The DRIVE buttons (IDG disconnect) were not pressed: lasting |
| Fuel | six pumps, crossfeed A and B, left and right transfer, center tank feed; their fault and OPEN lights | every row off and on; the crossfeed OPEN legend follows its valve | iniBuilds' variables cross the inner and outer names (L TK MAIN is `INI_OUTER_TANK_LEFT`); rows follow the buttons. Refuel through the OIS: LOADSHEET, fuel load, SET FUEL, Instant Refuel |
| Hydraulics | four engine pumps, four SUPPLY buttons; fault, OFF and overheat lights | each pump off and on | the SUPPLY buttons (fire shutoff valves) only ever set 1: not pressed in a test pass |
| Fire | APU, ENG 1 and ENG 2 fire buttons, five agents, fire test; 13 lights | FIRE TEST held 3 s lights all thirteen (`A350WritePlan.TestHoldMs`) | the fire buttons and agents latch: not pressed in a test pass |
| Engine Manual Start | ENG 1 and ENG 2 man start | both on and off | |
| Ventilation | avionics extract, cabin fans, cooling; two fault lights | each off and on | |
| Cargo Air Conditioning | aft, forward and bulk isolation valves, bulk and forward temperature knobs, heater; isolation valve and heater fault lights | every button; the knobs land on the value typed | an isolation valve's upper legend lights while the valve and its button disagree |
| Cargo Smoke | agent to forward, agent to aft; smoke and DISCH lights, bottle 1-3 discharged | | the agents discharge a bottle: not pressed in a test pass |
| Cabin Smoke | IFEC; its smoke light | off and on | |
| Ground Hydraulics | green and yellow electric pumps and their overrides; fault and override ON lights | every row; an override lights its ON legend | |
| Cabin | landscape camera, mobile communication; lavatory occupied light | both | |
| OIS | cabin data to OIS, gatelink, OIS data to avionics | each | |
| Surveillance | takeoff surveillance, ROW/ROP | both | |
| Maintenance | twelve rows incl. the BAT selector (EMER 1, EMER 2, OFF, BAT 1, BAT 2); refuel end and oxygen reset lights | every row and position | the BAT selector picks what the BAT voltage display shows |

Indicator lamps with no control of their own sit on their panel after its button lights (`A350PanelLamps.Indicators`): cargo bottle 1-3 DISCH, avionics smoke, MLG bay fire, IRS on battery, lavatory occupied.

### Glareshield panels, live-tested (A350-900, 1.2.7)

| Panel | Rows | Verified | Notes |
| --- | --- | --- | --- |
| FCU | AP 1/2, A/THR, FD, LOC, APPR, ALT, SPD/MACH, HDG-V/S / TRK-FPA, metric altitude, TRUE/MAG; the four knobs' push, pull and turn; altitude 100/1000; both sides' chrono, loudspeaker, HUD brightness, crosswind and declutter, master caution and warning, autoland, ATC message, sliding table light; FCU brightness; the four windows as status lines | every row; the knobs land on the value typed | windows (`A350FcuWindows`): `INI_AIRSPEED_DIAL` holds knots or Mach (Mach below 5), TRK-FPA swaps to `INI_TRK_DIAL` / `INI_FPA_DIAL`, `_DASHED` and `_DOT` (managed) flags; the heading truncates and V/S carries its sign, as drawn. Turns step 1 kt or 0.01 Mach, 1°, 1000 ft (100/1000 selector), 100 ft/min or 0.1°. AUTOLAND is a light test the cockpit returns within a second, so its rows press (`A350PanelLayout.MomentaryToggles`) |
| EFIS Captain | ND mode, ND range, baro knob (push, pull, turn), baro unit, CSTR, WPT, VORD, NDB, ARPT, VOR 1, WX, TERR, TRAF, VOR 2, LS, taxi, VV; each button's light; the baro as a status line | every position and button; the baro line followed a turn, hPa/inHg, STD (push) and QNH (pull) | the baro is the sim's `KOHLSMAN SETTING MB:1`, `XMLVAR_Baro1_Mode` (3 STD) and `XMLVAR_Baro_Selector_HPA_1` (1 hPa); the map's state variable for the baro knob is the unit flag, so the knob has no value line of its own. A filter button's row reads its light ("Captain CSTR: On", `TryDescribeControlState`). ZOOM ranges from the manual (5 NM down to 0.2 NM; the middle three inferred). The draw emulator answers sim variables with defaults (it drew QNH 1013 while the sim held 1009): never read a sim-variable value off an emulated draw |

## Manual coverage (Airbus A350 Manual 1.5, in the package's `Resources`)

Every section's controls and displays use one of the paths above: cockpit interaction (push, pull, scroll), the OIS (FLT OPS PLAN/STS, LOADSHEET, charts list, T.O/LDG PERF with SYNC and SEND TO FMGS, AOC, GROUND incl. GSX, MAINTENANCE, SITUATIONS, FAILURES, AUTOSAVE, PANEL STATES, OPTIONS incl. 3RD PARTY), the FCU and its managed/selected modes, the MFD and KCCU (menu bar, interactive F-PLN, vertical revisions, C/L MENU checklists), EFIS CP and baro preset, ANF and BTV arming, the noise function (MFD PERF T.O), thrust levers, RMP tuning and squawk, the MFD SURV page (DEFAULT SETTINGS, CONFIRM), the HUD, sensed and not-sensed ECAM procedures, deferred procedures, STATUS, and the automatic emergency descent.

Exercised live: panels, FCU, EFIS, RMP, MFD INIT entry, ND pop-up, ECAM sensed/ABN/STATUS, failures, OIS T.O PERF entry, FAP navigation, and in a second pass:

- **A failure cascade.** OIS `AC 1 230V + 115V BUS FAULT + DC 1 BUS FAULT` gave a WD queue (A-ICE ENG 1 VLV OPEN with its steps and a 40 s timer, SURV SYS 1, CDS CAPT OUTER DU, F/CTL FLAP SYS 1, EXT LT NAV LT 1, two fuel pumps, PART SPLRs, SEC 3, PRIM 3 with its reset question, the two-line ELEC alert, `*F/CTL` and `*FUEL` secondary pages). The check-off turned the boxed steps white; CLEAR worked down the queue to LIMITATIONS and STS; the SD STATUS showed INOP SYS in two columns over several pages ("PRESS STS FOR NEXT STATUS PAGE"); RESET ALL on the OIS cleared it. A procedure's unticked action box is a 15 × 20 `DrawFrame` beside the line (cyan, white once done).
- **DEFRD.** The page reads ("DEFERRED PROCEDURES LIST", "NO DEFERRED PROCEDURES"); none of the OIS's ground failures creates a deferred procedure. Its entries draw through the same item code as the ABN list.
- **MFD checklists.** C/L MENU lists the ten checklists as `CheckListMenuItemUIButton`s; BEFORE START shows pilot-confirm items in cyan with `____`, sensed items green (BEACON ON), RESET, C/L COMPLETE, C/L MENU; a click ticked PARKING BRAKE to "PARKING BRAKE : CHECKED" in green.
- **F-PLN revisions.** The F-PLN rows read with their cells as `BaseFlightPlanButton`s; a click on VCBI opened the revision menu (FROM P.POS DIR TO, INSERT NEXT WPT, DEPARTURE, OFFSET, AIRWAYS, NEW DEST, CONSTRAINTS, CMS, STEP ALTS in white; DELETE, ARRIVAL, OVERFLY, HOLD, ENABLE ALTN in grey).
- **Printouts.** MFD DATA → PRINTER reads; its AUTO PRINT boxes are `UIRectangleCheckbox` with the state at element +64. OIS T.O PERF → COMPUTE → SEND TO FMS printed "FM ACTIVE TAKE-OFF DATA" as plain text on both printer views.
- **BAT display.** It is blank with BAT SELECTOR at OFF; at BAT 1 it draws 27.2 (as "272" and "."), at EMER 1 27.1.
- **HUD.** With `INI_HUD_LEFT_STATE` 1 the second HUD object draws (FD, speed and altitude, heading scale, GS, QNH), all green.

Still to do with the pilot signed in: ANF exit selection for BTV (needs Navigraph) and the SD mailbox (needs a Hoppie or SayIntentions logon; the OIS fields are the pilot's to fill).

## Variants

`presets`: A350-900 (`A359`), A350-900 ULR (`A359 ULR`), A350-1000 (`A35K`), each with and without cabin. There is no A350-1000 ULR in 1.2.7.
