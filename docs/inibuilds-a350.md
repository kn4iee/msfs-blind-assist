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

The Overhead, Glareshield, Instrument, Pedestal and Cockpit sections are offered; a section joins once each of its panels has been driven live (`IniBuildsA350Definition.CuratedSections`). The panels' lights come from the map's emissive rules (`A350PanelLamps`): a lamp belongs to the row whose id is the longest prefix of its node, reads On only while its rule holds and its bus's light power (`INI_AC_LIGHTS_FAILURE` / `INI_DC_LIGHTS_FAILURE`, 1 = powered) is on, and a legend that reads nothing but its own switch is left out.

A button row shows its state beside its name wherever the cockpit shows one (`TryDescribeControlState`, `A350ButtonStates`): a button with one light reads On or Off. A button with several reads its lit legends ("EXT 1: Available", "APU agent: Squib and discharged", "Green ENG 1 supply: Shut"), a master light's lower half reading On, and reads Off when none is lit. A button with no light reads the state it switches, as the FCU and EFIS show it: SPD/MACH (Knots, Mach), HDG-V/S / TRK-FPA, each baro unit (Hectopascals, Inches), each baro knob's push and pull (STD, QNH), and each FCU knob's push and pull (speed and heading Managed or Selected; altitude Managed while its managed dot shows; V/S Engaged while V/S or FPA is the pitch mode). Each row's label repaints when that state changes. Momentary keys with nothing to show (calls, chronos, tests, the ISIS keys, the RWY COND knob) read nothing. Checked live with `A350Probe labels <panel>` against the aircraft's own variables.

### Announcements

What changes on its own is spoken, as on the A380, and each spoken thing has its own Ctrl+M row in the A350 Monitor Manager (`A350MonitorManagerForm`, list `UserSettings.A350DisabledMonitorVariables`):

- **Switches.** Every switch, selector and knob row is Continuous and announced, spoken by MainForm's generic path with its row's words ("Seat belts: On", "BAT 1: Off"); the pilot's own pick is not repeated (the echo gate, [CORE-8]). Spring and press rows are not listed.
- **Lights.** The definition speaks them as the cockpit lights them, rule × bus light power (`A350LampBoard`), at a batch end in one netted sentence (`A350LampSpeech`, the iniBuilds A300's timings: 1.5 s gather, 3 s off-hold so a flash reads once, 2.5 s settle after a light power change). A light's first value is a silent baseline; three batch ends after a connect or flight load the lights still unread are seeded from the cache. The master caution and warning speak "on" once (the captain's upper legend, `MasterSpeakers`); the other six legends are echoes with no row. A pressed button's own light is left to its label for 3 s (`PressedLampMs`).
- **Mutes.** A switch row is muted at MainForm's generic gate; a light inside the definition (`IsMuted`). A delivery that changes several lights (a light power flag, a shared input) is exempt from MainForm's mute wrap and checks each light's own row (`IsMuteWrapExempt`, [VAR-8]).

Live 2026-10-27 (`A350Probe listen <seconds>`, which applies both of MainForm's gates; `A350_MUTE=key,key` unticks rows in memory only): seat belts, no smoking and BAT 1 moved from outside were spoken, and with seat belts and the BAT 1 fault light muted, neither said anything while BAT 1 and the master caution still did. Earlier: "BAT 1 fault light on/off", "A/BRK light off", "Master caution on" (its reset silent).

### Overhead subpanels, live-tested (A350-900, 1.2.7)

Panels take the FlyByWire A380's names and order wherever the two cockpits share a panel (`A350PanelLayout.PanelOrder`); a control iniBuilds files under another group moves to the panel the A380 puts it on (`ControlPanels`).

| Panel | Rows | Verified | Notes |
| --- | --- | --- | --- |
| ELEC | BAT 1/2, BAT EMER 1/2, bus tie, commercial 1/2, ELM, EXT 1/2, galley, GEN 1A-2B, APU GEN, DRIVE 1A-2B, PAX SYS, side 1 and 2 isolation, EMER GEN, RAT MAN ON; 24 lights | every switch off and on; with the OIS GPU connected (GROUND, GPU), EXT 1 and 2 each on line and off | a battery's FAULT legend lights a moment after it goes off. The DRIVE buttons (IDG disconnect) were not pressed: lasting RAT MAN ON is not pressed in a test pass: it deploys the RAT |
| APU | master switch, start; master fault, start AVAIL and ON lights | master on, start: the APU ran up and AVAIL lit within a minute | |
| Fuel | six pumps, crossfeed A and B, left and right transfer, center tank feed, fuel jettison arm and active; their fault and OPEN lights; the jettison option line | every row off and on; the crossfeed OPEN legend follows its valve | iniBuilds' variables cross the inner and outer names (L TK MAIN is `INI_OUTER_TANK_LEFT`); rows follow the buttons. Refuel through the OIS: LOADSHEET, fuel load, SET FUEL, Instant Refuel. Jettison is an equipment option (`INI_OPTION_FUEL_JETTISON`, OIS options page): without it the cockpit hides its buttons and `UpdateFuelJettison` clears all three variables every frame, so MSFSBA refuses a press aloud and the status line says "Not fitted" (`A350Options`); with it fitted both rows and the valve (OPEN legend) were driven |
| Hydraulics | four engine pumps, four SUPPLY buttons, green and yellow electric pumps and their overrides; fault, OFF, overheat and override ON lights | each pump off and on; an override lights its ON legend | the SUPPLY buttons (fire shutoff valves) only ever set 1: not pressed in a test pass |
| Bleed Air | crossbleed, ENG 1 and 2 bleed, APU bleed; their fault lights | every position | |
| Air Conditioning | windshield defog, cockpit and cabin temperature knobs, ram air, packs, hot air, air flow; their fault lights | every position | the temperature knobs (0-100) are written by the systems module only through the panel-state presets; what they select is on the SD COND page |
| Pressurization | cabin altitude and V/S targets (typed fields) and modes, ditching; the two targets as status lines | modes to MAN and back; each target typed through its range ends (-2000 and 13500 ft, -1500 and +2500 ft/min) and rounded as the knob rounds | the knob steps 100 ft and 50 ft/min and works in AUTO too (`CabPressSystem::*_TARGET_*_HANDLER`, no mode check); the target is used only in MAN. An encoder's value is a status line on its panel (`A350Readouts`) |
| Ventilation | avionics extract, cabin fans, cooling; two fault lights | each off and on | |
| Cargo Air | aft, forward and bulk isolation valves, bulk and forward temperature knobs, heater; isolation valve and heater fault lights | every button; the knobs land on the value typed | an isolation valve's upper legend lights while the valve and its button disagree |
| Anti Ice | wing, ENG 1, ENG 2, probe and window heat; three fault lights | each on and off | |
| Fire | APU, ENG 1 and ENG 2 fire buttons, five agents, fire test; 13 lights | FIRE TEST held 3 s lights all thirteen (`A350WritePlan.TestHoldMs`) | the fire buttons and agents latch: not pressed in a test pass |
| Cargo Smoke | agent to forward, agent to aft; smoke and DISCH lights, bottle 1-3 discharged | | the agents discharge a bottle: not pressed in a test pass |
| Cabin Smoke | IFEC; its smoke light | off and on | |
| Oxygen | crew supply, mask man on | both rows, both positions | MASK MAN ON drops the cabin masks; its set code ignores its guard |
| Calls | nine call buttons, EMER; EMER call light | every button | the systems read only FWD and PURS; the other calls are chimes. EMER CALL lights only on a cabin call |
| Evacuation | EVAC command, EVAC horn off, EVAC CAPT and PURS; EVAC light | every row; EVAC command lights the EVAC legend (`INI_EVAC_LIGHT`) within seconds and puts it out when off | each guarded button's set code ignores its guard |
| Signs | EMER exit light, no mobile, no smoking and no PED, seat belts | every position | the signs reach `INI_NO_SMOKING_ON` / `INI_SEATBELTS_ON` |
| Wipers | captain and first officer wiper (6 positions), both rain repellents (hold) | every position of both selectors; the wipers sweep (`WIPER_ANIMATION_CPT`/`_FO`); each repellent holds 1 while pressed | the repellent variable is read only by the cockpit sound: a press is a spray sound and nothing else, for a sighted pilot too |
| ADIRS | ADIRS 1-3 (OFF, NAV, ATT), IR 1-3, ADR 1-3; their fault and OFF lights | every IR and ADR; ADIRS 3 through every position; ADIRS 1 and 2 to ATT and back | an IR off drops its IRS's alignment flag (`INI_IRSn_ALIGNED`) until it is back on; ADIRS OFF loses the alignment, which restarts at NAV. The ADR 2 and 3 FAULT lamps read `INI_ADF2_FAULT` / `INI_ADF3_FAULT` (iniBuilds' spelling) |
| Flight Control Computers | PRIM 1-3, SEC 1-3; their fault lights | each off and back on | |
| Engine FADEC and Manual Start | ENG 1 and 2 FADEC GND PWR, ENG 1 and 2 man start | each on and off | |
| Recorder and Misc | ACMS trigger, DFDR event, RCDR GND CTL, CVR test, CVR erase, ELT (TEST/RESET, ARMED, ON), ELT guard | the four buttons press; every ELT position | RCDR GND CTL flips and the aircraft clears it at once: `GENERAL_SYSTEMS::Update` sets `INI_GND_CTL` to 0 with an engine at N2 52 % or more or off the ground (`AcfVariables` 3, SIM ON GROUND), and it did so on the ground with both engines stopped. A click runs the same set code, so this is the aircraft. ELT TEST/RESET is taken only with the guard open (`INI_ELT_SWITCH_GUARD` 1); otherwise the switch springs back. The guard's tooltip words are the click's action (OPEN at 0) |
| Surveillance | takeoff surveillance, ROW/ROP | both | |
| Cabin | landscape camera, mobile communication; lavatory occupied light | both | |
| OIS | cabin data to OIS, gatelink, OIS data to avionics | each | |
| Maintenance | ten rows incl. the BAT selector (EMER 1, EMER 2, OFF, BAT 1, BAT 2); refuel end and oxygen reset lights | every row and position | the BAT selector picks what the BAT voltage display shows |
| Interior Lighting | annunciator light, standby compass light, three occupant light knobs, coat stowage light | every position; the knobs land on the value typed | |
| Exterior Lighting | beacon, landing, logo, nav, nose, strobe, wing | every position | beacon and wing follow on the sim's own light state; the nav lights are drawn from `INI_LIGHTS_NAV` by the model, so the stock LIGHT NAV stays on whatever the switch |

Indicator lamps with no control of their own sit on their panel after its button lights (`A350PanelLamps.Indicators`): cargo bottle 1-3 DISCH, avionics smoke, MLG bay fire, IRS on battery, lavatory occupied.

### Glareshield panels, live-tested (A350-900, 1.2.7)

| Panel | Rows | Verified | Notes |
| --- | --- | --- | --- |
| FCU | AP 1/2, A/THR, FD, LOC, APPR, ALT, SPD/MACH, HDG-V/S / TRK-FPA, metric altitude, TRUE/MAG; the four knobs' push and pull; the Speed, Heading, Altitude and VS/FPA typed fields; altitude 100/1000; FCU brightness; the four windows as status lines | every row; each typed field in knots and Mach, heading and track (350 to 010, 360), 100 and 1000 ft increments, V/S and FPA, and every refusal; the stock autopilot targets followed | windows (`A350FcuWindows`): `INI_AIRSPEED_DIAL` holds knots or Mach (Mach below 5), TRK-FPA swaps to `INI_TRK_DIAL` / `INI_FPA_DIAL`, `_DASHED` and `_DOT` (managed) flags; the heading truncates and V/S carries its sign, as drawn. Turns step 1 kt or 0.01 Mach, 1°, 100 or 1000 ft (100/1000 selector), 100 ft/min or 0.1°. AUTOLAND is a light test the cockpit returns within a second, so its rows press (`A350PanelLayout.MomentaryToggles`) |
| EFIS Captain | ND mode, ND range, baro knob (push, pull), QNH (typed, in the side's unit), baro unit, CSTR, WPT, VORD, NDB, ARPT, VOR 1, WX, TERR, TRAF, VOR 2, LS, taxi, VV; each button's light; the baro as a status line | every position and button; the baro line followed a turn, hPa/inHg, STD (push) and QNH (pull); QNH typed in hPa and inches, and refused on STD | the baro is the sim's `KOHLSMAN SETTING MB:1`, `XMLVAR_Baro1_Mode` (3 STD) and `XMLVAR_Baro_Selector_HPA_1` (1 hPa); the map's state variable for the baro knob is the unit flag, so the knob has no value line of its own. A filter button's row reads its light ("Captain CSTR: On", `TryDescribeControlState`). ZOOM ranges from the manual (5 NM down to 0.2 NM; the middle three inferred). The draw emulator answers sim variables with defaults (it drew QNH 1013 while the sim held 1009): never read a sim-variable value off an emulated draw |
| EFIS First Officer | as the captain's | every position and button; the baro line as the captain's | `XMLVAR_Baro2_Mode`, `XMLVAR_Baro_Selector_HPA_2`, `KOHLSMAN SETTING MB:2` |
| Warnings | both sides' chrono, master caution, master warning, autoland and ATC message; their lights | every button | the A380's Warnings panel; AUTOLAND as on the FCU row |
| HUD | both sides' HUD brightness, crosswind mode (Norm, X-wind, the right switch's tooltip words) and declutter (Off, On) | with the HUD fitted and deployed (captain's handle): every row on both sides, restored; with it not fitted, a row is refused aloud | the HUD is an OIS option (`INI_IS_HUD`, `A350Options`): without it the cockpit shows its non-HUD skeleton and none of these keys, so MSFSBA refuses them ("HUD is not fitted. It is an option on the OIS options page.") and the panel's status line says "Not fitted". A declutter key's input event only animates the key; its click code flips `INI_FCU_HUD_DECLUTTER_LEFT`/`_RIGHT`. The map makes it a two-position selector on that variable (`CLICK_SWITCHES` in the generator) and MSFSBA writes what the click code writes (`A350WritePlan.ClickWrites`, [A350-7]) |
| Loudspeakers and Table Lights | both sides' loudspeaker and sliding table light knobs | the knobs land on the value typed | |

### Instrument panels, live-tested (A350-900, 1.2.7)

| Panel | Rows | Verified | Notes |
| --- | --- | --- | --- |
| Gear | gear lever, anti-skid, brake fans; brakes hot light; the brake fans option line | every position; the brake fans run (`INI_Brake_Fans_ANIM`) | UP is refused on the ground by the aircraft's own interlock and said ("Gear lever did not move"). The lever writes the aircraft's key events (`INI.GEAR_UP`/`DOWN`), read on `INI_GEAR_HANDLE_STATUS` |
| Autobrake | runway condition knob push and pull, runway condition (a choice), runway condition or braking action, A/BRK; A/BRK light | A/BRK arms and disarms (`INI_AUTOBRAKE_ARMED`; level 4 = RTO on the ground); every runway condition picked and checked in the WASM's own index | `A350RunwayCondition`: the pick is an index the WASM keeps to itself (`GEAR::Update`, 0 DRY to 5 ICE, in its memory), moved one step a click only while the knob's menu is open on the SD (pull). Five clicks up land on DRY whatever was picked, then one down per code below 6, then a push; 300 ms between clicks (at 150 ms a long frame lost one). The words are the SD menu's. `INI_AUTOBRAKE_LEVEL` stays 0 on the ground |
| ISIS | each unit's STD/CHANGE, LS/DIR TO, menu, minus, mode, plus; the captain's QNH (typed); the first officer's set/sel knob; the ISIS QNH setting as a status line; the first officer ISIS option line | each key's command is consumed by the ISIS (`INI_ISIS1_*`, `INI_ISIS2_*` back to 0); the QNH typed in hPa and inches | the keys are holds whose set code acts only on 2. What the ISIS then shows (STD, LS, its menu) is its gauge's own state, no variable: the emulated draw answers sim variables with defaults, so it is read with the in-app display reader. The captain's baro is the sim's `KOHLSMAN SETTING MB:3` (a click moved it 1009.1 to 1010.1); the first officer's unit (an option, `INI_OPTION_FO_ISIS`) changes no sim baro |
| Source Switching | FMS source, air data source | every position | |
| Display Power | captain and first officer OIS displays, captain and first officer outer displays, center lower and center upper displays | each off and on | named by what went dark: `INI_EFB1_STATE` / `INI_EFB2_STATE` (the OIS displays), the captain's EFIS draw (PFD and ND), `INI_MFD_STATE` and the MFD draw (center lower), the centre draw (E/WD and SD, center upper); a display powered again runs its SAFETY TEST |
| Captain / First Officer Side Panel | pedal light, OIS switch (EFB, NEUTRAL, AVNCS), OIS view offside, console light, map light; OIS view offside INOP and on lights | every row; the knobs land on the value typed | the OIS switch springs back to NEUTRAL, and only the cockpit sound reads it. OIS VIEW OFFSIDE shows the other side's OIS, one side at a time: the other side's button then reads INOP |
| Captain / First Officer Individual Panel | lateral vent and defog, foot vent, heater (HIGH, LOW, OFF) | every row and position | |

### Pedestal panels, live-tested (A350-900, 1.2.7)

The A380's pedestal panels in its order, the A350's emergency descent and trims beside them. The levers are hand-written
(`A350Levers`, "Pedestal levers" below). The RMPs (192 keys and a WASM display) and the KCCU wheels have plans of their own
and are not placed yet (`A350PanelLayout.DeferredPanels`).

| Panel | Rows | Tested | Notes |
|---|---|---|---|
| Engines | engine mode selector (Crank, Norm, Ign start), ENG 1 and 2 master; each master's FIRE and FAULT lights | every selector position; each engine shut down with its master and restarted with IGN START and its master (N2 59.7 % again); the masters' FIRE lights under the fire test | iniBuilds' master tooltips are swapped (the OFF key reads ON): a running engine reads `INI_MIXTURE_RATIO*_HANDLE` 1, so the words are pinned Off/On |
| Thrust Levers | All thrust levers, Thrust lever 1 and 2 (Reverse, Reverse idle, Idle, Climb, Flex/MCT, TOGA), left and right A/THR disconnect | every detent on each lever and on both, with the parking brake set | the A380's rows and words; spoken as the A380's ("All thrust levers Climb"). The A/THR disconnects fire `K:AUTO_THROTTLE_DISCONNECT`: for the flight test, with A/THR engaged |
| Emergency Descent | emergency descent; its ARMED light | pressed on the ground | for the flight test |
| Flaps and Brakes | flaps (Up, 1, 2, 3, Full), parking brake, brake accumulator reinflate, gravity gear extension cover, guards 1 and 2, gravity gear extension (Reset, Off, Down) | every flap position; the parking brake off and on; the whole gravity extension procedure (cover and guards open, Down, Off, Reset, Off, closed) | the handle's tooltip reads state n as key n+1 and iniBuilds has no third key: its words come from what it does (2 Down, taken only with the cover and both guards at 1; it rests at 1, Off). The cover's tooltip words are the click's action |
| Speed Brake | speed brake (Retracted, Quarter, Half, Three quarters, Full), ground spoilers (Disarmed, Armed); the handle's status line | every step, arm and disarm | spoken as the A380's, in tenths ("Spoilers 40 percent") |
| Trim | rudder trim left and right (nudges), rudder trim reset, pitch trim nose up and down (nudges); rudder and pitch trim status lines | nudges both ways and the reset, read back on the status lines | the switches trim while held: nudges, as on the A380 and the Fenix |
| ECAM Control Panel | System display page (the A380's picker), the page keys, all, status, video, abnormal and deferred proc, dispatch, more, T.O config, emergency cancel and its guard, both sides' clear and checklist, recall last and all, both ECAM wheels, the SD knob | every key; every page through the picker and through its key | the page is `INI_ECAM_ACTIVE_PAGE` (ENG 1, HYD 2, EL/DC 3, EL/AC 4, BLEED 5, COND 6, PRESS 7, FUEL 8, APU 9, F/CTL 10, DOOR 11, WHEEL 12, STS 14, VIDEO 16); the picker presses the page's key. The page keys' lights follow the page, so they are its echoes: shown, never spoken. The keys acting on the E/WD and SD (checklist, clear, recall, more, the wheels, SD knob push) are read back with the displays |
| Weather Radar | weather radar system 1 and 2, both sides' gain, elevation and VD azimuth (push auto, pull manual, steps) | the systems and every key pressed | the knobs' commands (`INI_WXR_*_UP`, `_PUSH`, …) wait for the radar, which the MFD SURV page switches on (`INI_WXR_ON`): their effect is read with the MFD |
| Transponder | G/S mode, TCAS above, below and TA only, transponder system 1 and 2; their lights | every row and light | ABV and BLW toggle `INI_tcas_mode` (2, 3, back to 1); TA only `INI_tcas_mode_pedestal` (2 TA/RA, 3 TA) |
| Cockpit | captain and first officer OIS on center and display cycle, dome, flood and integral lights, DU master brightness, DU master (Inhibit, On), cockpit door switch (Lock, Norm, Unlock), printer; the OIS on center INOP, door OPEN and FAULT lights | every row; the light knobs land on the value typed | an OIS on center button lights the other side's display cycle key. DU MASTER BRT's steps are consumed by the WASM, but nothing keeps `INI_CKPT_LT_DU_MASTER_BRT` (its tooltip's value), so it has no value line |

### Cockpit panels, live-tested (A350-900, 1.2.7)

iniBuilds' MISC area: what sits around the crew rather than on a panel. The cabin-side door handle and seven of the eight
printer paper clickspots repeat another row exactly, so they are not rows (`A350PanelLayout.HiddenControls`).

| Panel | Rows | Tested | Notes |
|---|---|---|---|
| Doors and Hatches | cockpit door lock (Unlocked, Locked), cockpit door (Closed, Open), avionics bay hatch (Closed, Open) | every row; with the door open, locking closed it | the door's position words come from what it does (`INI_COCKPIT_DOOR`); the pedestal's door control is the "Cockpit door switch" (Lock, Norm, Unlock) |
| Sun Shades | captain and first officer sun shade (0 to 100), side and rear sun shades and the center one (Stowed, Deployed) | every row; the main shades land on the value typed | |
| Oxygen Masks | for the captain, first officer, third and fourth occupant: mask stowage (Closed, Open), N/100% (Normal, 100%), mask test and emergency pressure (held) | every row | the stowage's tooltip names the click (the masks rest closed at 0) |
| HUD Units | captain and first officer HUD cover (Off, On) and HUD (Stowed, Deployed) | with the HUD option fitted, each handle and cover both ways; without it, refused aloud | the HUD option (`INI_IS_HUD`) gates them as the HUD panel; the handle's tooltip names the click (1 drew the HUD) |
| Tillers and Sidesticks | captain and first officer sidestick takeover, captain and first officer tiller (Connected, Disconnected) | every row; both tillers flip one `INI_TILLER_DISC` | the takeover buttons fire the aircraft's `'INI.AUTOPILOT_OFF'` key event; the A350 engages no autopilot on the ground, so their effect is for the flight test |
| Misc | dome light override, printer paper tear off, pen | every row | |

### Flying the A350 for a test

`tools/A350Probe rpn "code#ms;…"` sends calculator strings through MSFSBA's own sender with exact pauses, for timing the MCP's
round trips cannot pace. The thrust levers go straight to an angle written into `L:INI_TLA1_REQUEST` / `INI_TLA2_REQUEST`
(the cockpit's own lever-drag code writes it; the aircraft takes it and puts the request back to -1), so a flight test sets
CLB with `25 (>L:INI_TLA1_REQUEST) 25 (>L:INI_TLA2_REQUEST)`. The axis events (`K:THROTTLE1_AXIS_SET_EX1`) moved the lever only
part of the way per event. Put back on the ground with the gear lever still up, the aircraft sits on its belly until the gear
is down and it is placed again; a placement also lets the parking brake go, so set it again at once.

Flight test, live 2026-10-27, all through MSFSBA's own rows (`A350Probe keep`), placed at 12,000 ft and 250 kt: All thrust
levers to Climb, then the FCU A/THR button, engaged A/THR (N1 87 %, `INI_ATHR_LIGHT` 1); AP 1 engaged from the FCU; each
A/THR disconnect button put the A/THR light out and each sidestick takeover disconnected AP 1 (re-engaged between); typed FCU
speed and altitude with the knob's pull flew an open climb; the speed brake extended the spoilers (`INI_SPOILERS_OUT` 1,
"Spoilers 50 percent") and retracted; the gravity gear extension (cover, both guards, Down) let all three gear legs fall to
100 % with the gear lever still up; EMER DESCENT armed its function (the ARMED light) and disarmed it on a second press (the
automatic descent itself starts only on a depressurisation). Put a test's FCU targets back afterwards: a speed left at 100
knots from ground tests had the A/THR pulling idle in the air.

### Pedestal levers (`A350Levers`, live 2026-10-27)

The map lists the levers as "none": each bypasses its input event, so they are written in C# the way the cockpit's own
lever code writes them, with the A380's rows and words (the A320 and Fenix agree where they overlap):

| Row | Positions | Written with | Read from |
|---|---|---|---|
| Flaps | Up, 1, 2, 3, Full | `n×4096 (>B:AIRLINER_Flaps_Set)` (its set code fires `'INI.FLAPS_SET'`); the aircraft picks 1+F itself | `INI_FLAPS_HANDLE_INDEX` 0 to 4 |
| All thrust levers, Thrust lever 1, 2 | Reverse (-20), Reverse idle (-6), Idle (0), Climb (25), Flex/MCT (35), TOGA (45) | `tla (>L:INI_TLAn_REQUEST)` | `INI_AUTOTHRUST_TLA:n`; All reads lever 1 |
| Speed brake | Retracted, Quarter, Half, Three quarters, Full | `'INI.SPOILERS_ARM_OFF' (>F:KeyEvent) x 'INI.SPOILERS_SET' (>F:KeyEvent)` (x 0 to 16384) | `INI_SPOILERS_HANDLE_POSITION` 0 to 1 |
| Ground spoilers | Disarmed, Armed | arm: `0 'INI.SPOILERS_SET'` then `'INI.SPOILERS_ARM_ON'`; disarm: `'INI.SPOILERS_ARM_OFF'` | `INI_SPOILERS_ARMED` |
| Parking brake | Off, On | its input event (the toggle `K:PARKING_BRAKES`), only when the state differs | stock `BRAKE PARKING POSITION` |
| Rudder trim left / right (nudge) | press | the switch at 2 (left) or 0 (right) for 300 ms, then 1: about 0.43 degrees | `RUDDER_TRIM_ACTUAL_ANGLE`, negative left |
| Rudder trim reset | press | its input event (`'INI.RUDDER_TRIM_RESET'`) | the trim back to 0 |

Reverse idle is -6: the commanded N1 (`INI_ENGINE1_TLA_N1`) rises 0.95 per degree from idle (21.3) to -6 and 1.29 per degree
from there to -20 (45). The rudder trim switch trims about 1.6 degrees a second while held, so it is two nudges, as on the
A380 and the Fenix, never a combo left trimming. The stock `SPOILERS HANDLE POSITION` does not follow the A350's handle (51 %
against 0.366), so there is no fine speed brake slider; quarters stand in for it. The stock fuel tanks and rudder trim do not
follow the aircraft either: refuel through the OIS (LOADSHEET, FUEL LOAD, SET FUEL, Instant Refuel).

Pitch trim is two nudges too (nose up, nose down), each holding both pitch trim switches for 1 s (0 nose up, 2 nose down,
1 off): the THS trims about 0.19 degrees a second, so a nudge is about 0.2 degrees. Either switch alone trims as well; the
A350 sets its takeoff trim itself (`FMGS_TO_TRIM_DEG`). Status lines beside them (`A350StatusLines`, shown, never spoken):
"Rudder trim: 1.2 degrees left" (or Neutral), "Pitch trim: 1.3 degrees up" (the stock `ELEVATOR TRIM POSITION`, which
follows the A350's `ELEV_TRIM_RATIO`), "Speed brake handle: 37 percent". The levers speak as the A380's do (`A350LeverSpeech`): "Thrust lever 1 Climb", "All thrust levers
Climb" once both sit there, nothing mid-travel; "Spoilers 40 percent", "Spoilers full", "Spoilers retracted" by tenths; each
under its own Ctrl+M row, after a silent baseline seeded from the cache after a load. Flaps, ground spoilers and the parking
brake are spoken by the generic path ("Flaps: 2").

### Typed values (`A350TypedEntry`)

A knob whose value the cockpit shows is one typed field in place of its increase and decrease rows, as the released aircraft type theirs: the FBW A320's FCU panel fields (Speed, Heading, Altitude, VS/FPA, validated and worded by `FcuValueEntry`, confirmed "FCU speed 250, selected") and the FBW A380's QNH ("QNH (in Capt unit)", the side's unit, "Altimeter set …") and pressurization fields. The value is landed by the aircraft's own click: the variable the click steps from is written one step short, then the encoder is clicked once toward it, so the aircraft's handler sets it with every side effect a turn has; a read-back after the click corrects it directly when the click landed elsewhere, and the value the cockpit then shows is said.

- Live 2026-10-26: the FCU dials are the FCU's state (`INI_HEADING_DIAL` 100 then a click read 101, and `AUTOPILOT HEADING LOCK DIR` 101; the altitude, speed, track and FPA dials alike). L:var names are case-insensitive (`INI_AIRSPEED_DIAL` and the WASM's `INI_Airspeed_Dial` are one variable).
- The aircraft's limits (WASM 1.2.7): `clampSpeed` 100-399 kt, `clampMach` 0.10-0.99, `ClampAltitude` 100-49000 ft, the V/S handlers ±6000 ft/min and ±9.9°.
- Each knob click sets a `*_COMMAND` / `*_INC` L:var the WASM consumes once per frame, so several clicks in one frame are one click, and fast clicks accelerate (`getRotationSpeed`): a value is never landed by counting clicks.
- Checked in flight (10,000 ft, AP 1 and FD on, 2026-10-26). A turn while a window is managed or dashed is a preset the pull then uses, and the mode stays: the speed knob while managed sets `INI_Preset_Dial` (FCU::Draw shows it for 45 s after `FCU_SPD_TIMER`, and the WASM drops `INI_FCU_SPD_DASHED` exactly then), the heading knob in NAV sets the dial with NAV kept, the V/S knob while dashed shows a preview (`FCU_VVI_TIMER`, about 45 s) with the pitch mode kept; each pull selected the preset. So a typed value into a managed or dashed window is confirmed "…, preset", and the status lines read the preset ("260 knots, preset", "Heading 310, preset"). Modes as the A350 numbers them: `INI_ROLL_MODE` NAV 1, HDG 2, TRK 11 (10 on the ground); `INI_PITCH_MODE` V/S 8, FPA 11, managed ALT 12. The A350's AP is `INI_ap1_on`; the stock AUTOPILOT MASTER does not follow it.
- The baro is the sim's own `value side r (>K:2:KOHLSMAN_SET)`, the event the A350's own `Baro_Inc_handler` fires; the click then runs `LinkBaros`, which copies the side to the other side and the ISIS when the OIS "linked instruments" option (`INI_LINKED_INSTRUMENTS`) is on. The cockpit floors hectopascals (the knob's tooltip, `flr '%dhPa'`): 1015.81 reads 1015, and so do MSFSBA's lines.
- On STD the knob sets a preset QNH, in flight only (on the ground it shows none). The WASM keeps it in its own memory (`INI_BARO_PRESET_CPT`/`_FO` are its output, whole hectopascals or hundredths of an inch, shown while `…_SHOW`): the first click shows it, each click while it shows is a step, and clicks one MCP round trip apart let it lapse. The pull sets the QNH from it (29.85 stepped to 29.89, the pull set 29.89). A typed QNH on STD is therefore stepped to by clicks 300 ms apart, read between them (`A350TypedStepper`), and confirmed "Captain altimeter preset 1015 hectopascals"; the status line reads "STD, preset 1015 hectopascals". Where no preset shows, it is said.
- The cabin targets drive the A350's own cabin model, not the stock pressurization variables (frozen): in MAN, a typed 3,000 ft target moved the cabin from 1,250 ft at 450 ft/min (CAB PRESS page, read with the display reader). Switching to MAN seeds the target from the cabin.
- The 100/1000 ft altitude increment is a value the WASM keeps to itself: a typed altitude with the 1000 ft increment lands on the 1000 ft grid and the read-back writes the typed value.

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

The HUD is an OIS option on every variant, not a ULR feature: the cockpit's `SKEL_INT_COCKPIT_VISIBILITY_*_ULR_VIS` skeletons show when `INI_IS_HUD` is 1 (`900_ULR` with `INI_IS_1000` 0, `1000_ULR` with 1), the `NONULR` ones when it is 0. Only the HUD cockpit holds the HUD units, their covers and handles, and the glareshield's HUD keys.
