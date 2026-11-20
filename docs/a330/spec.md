# iniBuilds A330 (MSFS 2024): spec for the parity work

**Goal (owner, 2026-11-19).** Bring the built-in A330 "up to spec and parity with the rest of the
supported aircraft": the MCDU read with no OCR and no OCR training, the other displays read the same
way, and every known weakness resolved: the First Officer, missing panels and controls, inverted or
mislabeled controls, panels that are "not made up" and "faithful to the sim other than where ordering
matters to make it easier for a blind pilot to get to the most frequently used controls first in tab
order, and are in flow/checklist order", the FCU, and a walkthrough of the EFB "to make sure the EFB is
compliant and up to spec". The chocks and covers press just added to the EFB is the one exception to the
walkthrough, and it is removed before the pull request.

**Yardstick.** For how the aircraft's systems behave: the Fenix A320 and the FlyByWire A32NX (the app's
other Airbus types). For how the app presents things (windows, wording, keys, Ctrl+M rows): every
aircraft. Where a sighted pilot can see or do something in the A330's own cockpit or tablet, BA lets a
blind pilot do it in its own windows, unless a stated reason says otherwise.

**Plans:** `docs/a330/plans/00-index.md`. This file and the plans are deleted before the pull request
merges (plan 12).

## 1. The aircraft

- The A330-200 and -300 built into MSFS 2024, by iniBuilds: package `fs24-microsoft-aircraft-a330`,
  aircraft code `INI_A330`. Its package is encrypted on disk and is never decrypted.
- Its displays are drawn by the aircraft itself, not as web pages, so the Coherent debugger has nothing
  to read: both PFDs, both NDs, the E/WD, the lower ECAM, both DCDUs, both MCDUs, the ISIS and the small
  panels (FCU, radios, clock, DME, transponder, rudder trim, battery). The EFB is an HTML page.
- Controls are MSFS 2024 input events ([INI-1]); state is in `INI_*` L:vars.
- Nothing in the aircraft exports its screens: no MCDU client data (the same maker's A300 has one), no
  Comm Bus broadcast, and the methods the app uses on other aircraft do not apply.

## 2. Where it stands (measured 2026-11-19 and 2026-11-20, A330-200 (GE), on the ground)

The second day started cold and dark and went through batteries, external power, the IRS alignment and a
whole MCDU preflight with a route (KATL to KMCO, typed by hand), every lower ECAM page and the ND's modes.

### 2.1 Displays

- Today: the MCDU is read from pixels ([INI-4]): a forced pop-out, a camera change, the simulator
  brought to the front, and a glyph matcher trained from labelled captures (3 of its 39 training pages
  are in the repository). The PFD, ND and both ECAM displays have only the on-request AI read. There is
  no display window, and no E/WD, ECAM, DCDU or FMS message call-out.
- **A way to read them with no picture was proven** (owner approved, 2026-11-19). It is a separate
  program, the display reader, built from the team's private reference and placed beside the app. The
  app receives from it each display's drawn text (every character, its position, font and colour) and
  whether the frame is complete. How it reads is not in this repository.

| Display | Result of the proof (a slow script, one processor core) |
| --- | --- |
| MCDU, both sides | Whole page, exact, both fonts, colours. 52 pages of a full preflight read complete, each within 0.5 to 3 s; the busiest (the ARRIVAL list, 804 characters) in 1.5 s. Works with the simulator in an outside view, paused, and with the MCDU dimmed to zero: no pop-out, camera or focus. |
| E/WD | Complete in 2 to 9 s: engine fields, fuel on board, memos, and on batteries only four cautions with their action lines. |
| Lower ECAM | Every page its key gives (ENG, BLEED, PRESS, EL/AC, EL/DC, HYD, APU, COND, DOOR, WHEEL, F/CTL, FUEL) complete in 1.5 to 6 s. |
| ISIS | Complete, with its flags during alignment. |
| ND | Complete in 1 s with the IRS aligned and no route, and complete with the owner's uplinked route at 10 NM (16 items). At 20 and 80 NM with many more waypoints on the map it was NOT complete in the proof (at best 7 of 13 items in 10 s): plan 09's second go or no-go, by range. |
| PFD | Readable, but only with the proof run eight times side by side: 30 of its 31 items within 2.5 s, 37 of 38 with both flight directors on (one alone: 24 of 30 in 20 s). With the flight director on, the armed modes ALT and NAV were read as text. Plan 09's go or no-go stands until the built reader is measured. |
| DCDU | Nothing drawn while empty: not measured. |
| FCU, radios, clock, DME, transponder, rudder trim, battery | Not readable this way: redrawn only when they change. Their values are variables. |
| Any display, aircraft cold and dark | Nothing is drawn: the reader has nothing to give, and says so. |

- The reader gives what is ON THE SCREEN. The aircraft draws a whole list (all ten runways, every SID)
  and shows a window of it; the frame holds the rows in the window only, as a sighted pilot sees them.
- The reader also gives the MCDU's scratchpad text at once, apart from the page.
- The page shown is `L:INI_FMC_PAGE1` / `_PAGE2` (1-based: 2 INIT, 22 MCDU MENU).
- A CLR press with FMS messages queued clears a message, not a scratchpad character.
- The scratchpad line shows only the newest FMS message. An entry error can sit behind older amber
  messages and never be shown: "ENTRY OUT OF RANGE" was queued and invisible until the others were
  cleared. An entry can also be refused with no message at all.
- The self test of each display ("SELF TEST IN PROGRESS", "MAX 40 SECONDS") is drawn as text and read.

### 2.2 Panels

- The control map has 281 controls in 36 generated panels, plus five panels the definition fills itself.
  Its labels and position names are hand-written in the generator.
- **The aircraft's own names are available.** Its own strings (the tooltips a sighted pilot sees) give a
  title and position names for about 890 named controls. Example: BA says "IRS 1 mode: Off, Nav, Attitude"; the
  aircraft says "ADIRS1 IR MODE: OFF, NAV, ATT". The private reference reads them and hands this
  repository one data file of names (spec Q4).
- **About 245 of them are in BA's panels; 14 more are used by code; about 635 are not offered.**
  By their names, the largest groups: the third radio and audio panel (109), the standby instruments
  (35), each MCDU's keys (34 a side, pressed through L:vars instead), the first officer's radio and
  audio panel, each DCDU's keys (10 a side), TCAS (9), hydraulics (8), the leak-measurement valves (6),
  oxygen (6), emergency and evacuation (9), gravity gear extension (4), the autobrake buttons, the
  cockpit door, the clock, cargo, the maintenance panel. Only the captain's radio panel, audio panel,
  chronometer and master warning and caution are mapped today.
- The simulator lists 649 input events for the aircraft; that list is not in the repository, so no test
  can say every control is accounted for.
- Never probed live: the ADIRS, the fire handles and agents, the IDG disconnects, the RAT, the gear
  lever. Listed unverified: the parking brake's direction, the landing-light position words, the dome and
  wiper labels, several controls read through their own input event.
- **The simulator's own procedures are available.** The simulator has 39 procedures and checklists for
  this aircraft (preliminary and cockpit preparation, before start, engine start, after start, taxi, ...
  parking, securing, and their checklists), each item with its subject and its expected action. Their
  order is in the simulator's own checklist window. Their text is the simulator's and is not copied
  into this repository: the private reference holds it, and this repository holds only the order of BA's
  own controls and flow steps that follows from it.

### 2.3 FCU and autopilot

- Ctrl+H, S, A, V open value windows with the buttons beside each knob; Ctrl+P opens an autopilot
  window; Ctrl+B sets the altimeters. Values are written to the dial L:vars and read back.
- Open: the A/THR, AP, LOC and APPR presses are unproven in the air; the altimeter Standard press has two
  fallbacks nobody measured, and is unproven in a climb; a write is sent
  from a pool thread in the value windows; there is no call-out when a knob is turned on hardware; the
  definition lacks the autopilot-engage members the app's automation uses.
- Measured 2026-11-20 on the ground: the first officer's altimeter knob works as the captain's does
  (`AIRLINER_FO_BARO_SET_PULL` set 1 then 0 gave `XMLVAR_Baro2_Mode` 3, Standard; the PUSH the same way
  0, QNH). The aircraft publishes what each FCU window shows: `INI_FCU_SPD_DASHED`, `INI_FCU_SPD_DOT`,
  `INI_FCU_HDG_DASHED`, `INI_FCU_HDG_DOT`, `INI_FCU_ALT_DOT`, `INI_FCU_VS_DASHED` (flight director off:
  all 0; captain's flight director on with a flight plan and V2: speed and heading dashed with their
  dots, altitude no dot, V/S dashed).

### 2.4 First Officer

- 14 flows and 24 checklist groups with the same ids as the other Airbus profiles.
- Open: the engine start and shutdown flows were never flown; the ADIRS, flap, spoiler, parking brake and
  Standard writes are unmeasured; the automation manager is an empty stub; there is no gear confirmation
  ([FO-13]), take-off configuration verdict, ground-spoiler re-arm, emergency-exit-light sequence or
  center-pump handling; the profile is absent from the tests that sweep every profile
  (`FoCutGuardSweepTests`, `FoDepartureGroupsTests`, `FirstOfficerWindowsTests`).
- Measured 2026-11-20: the parking brake's input event is 0 = set, 1 = released
  (`B:AIRLINER_PARKINGBRAKE`; `BRAKE PARKING POSITION` followed both ways, chocks in). The simulator's
  38 procedures and their 669 items were captured in order from MSFS 2024's own tablet (Aircraft,
  Checklists), which the app's sim tablet reader opens with nobody touching the simulator.

### 2.5 EFB

- An agent reads the tablet's seven pages: Dashboard, OFP, Ground, Payload, Panel State, Take Off,
  Options. It has no tests and no captured fixtures. The definition has no `OnSwitchedAway`, so the
  tablet's connection is not closed on an aircraft switch.
- The temporary chocks and covers press ([INI-9]) is in the agent. The simulator tablet now in main
  (#321, merged into this branch on 2026-11-20) does not remove chocks or covers, so the press stays
  until plan 12.
- Measured 2026-11-20: with the SimBrief identifier box on the Options page empty, the Dashboard's
  "Import from SimBrief" does nothing and shows nothing. The window has to say why.
- With the identifier in: the Dashboard's import loads the OFP. The Payload page's import takes the
  OFP's numbers in the tablet's own unit: with the tablet in kilograms and an OFP in pounds, fuel
  106,797 lb became 106797 kg and the zero fuel weight hit the page's maximum, and the applied load is
  real (the simulator read 274,796 kg on an aircraft whose maximum is 230,000 kg). The unit is the
  pilot's setting on the Options page; BA notes the mismatch and never changes it (owner, 2026-11-20). The tablet sends take-off data to the FMS ("Send to FMS", confirmed on the
  MCDU) but never a route.
- **The route comes from the MCDU's INIT page: its request prompt, shown only while FROM/TO is empty**
  (the owner's own preflight, 2026-11-20). With the tablet's OFP imported, it fills INIT A (company
  route, alternate, flight number, cost index, cruise level) and the whole flight plan: the departure,
  every airway named on its legs, ocean coordinates, the STAR, the approach, the alternate.
- Two other ways exist and are worse: the aircraft takes the simulator's own flight plan at a flight's
  start and from MSFS 2024's tablet ("Send to avionics"), but drops every airway waypoint and ocean
  coordinate and picks wrong navaids of the same name; typing the route works. The ACARS FUNCTIONS
  request page is inoperative and most of the AOC menu does nothing.

### 2.6 App-wide parity

| Missing | Others that have it |
| --- | --- |
| Display windows on Output Alt+P, N, E, S, I and Ctrl+Shift+D, with the cockpit controls that change each display | Fenix, PMDG 737 |
| E/WD alert, memo, DCDU and ND message call-outs with their Ctrl+M rows | Fenix |
| FMS messages spoken with the MCDU window closed | FlyByWire A32NX |
| Hotkeys: ISIS, fuel info, gross weight, next waypoint, waypoint info, VS speed, aircraft windows, ECAM call-outs on and off | Fenix, FlyByWire |
| `GetVisualGuidanceProfile`, `TaxiTurnLeadSeconds`, `TakeoffCalloutFeedKey`, `EngageAutopilot` and `IsAutopilotEngaged`, `IsMuteWrapExempt` | Fenix, FlyByWire, Headwind |
| The definition in the all-aircraft test list (`ComboLabelCollapseTests.AllAircraft`) | every other aircraft |
| Comma-decimal culture tests (de-DE, sv-SE) | Fenix, FlyByWire |
| A changelog fragment (`aircraft`) | every aircraft at its merge |

### 2.7 Flight modes

- The FMA is spoken from mode numbers in `INI_*` L:vars. Six numbers were measured, on the ground; every
  other number's word is a guess from another aircraft, and an unknown number is said as a number.
- Measured 2026-11-20: the PFD draws its flight modes as text. At the gate with the captain's flight
  director on, the armed row read ALT and NAV in cyan; with it off there is no mode text.
- The aircraft publishes its flight phase: `INI_flight_phase` (lower case), `INI_EWD_FLIGHT_PHASE` and
  `INI_ECAM_PHASE` all read 1 at the gate on external power. Their values through a flight are not
  measured.

## 3. Requirements, by plan

| # | Requirement | Plan |
| --- | --- | --- |
| R1 | The branch's two commits get an independent review; its real findings are fixed. | 01 |
| R2 | Later plans add to the definition without two plans editing one file (seams, a file split by owner, tests split by owner). | 01 |
| R3 | The A330 is in every test that sweeps all aircraft, and passes them. | 01 |
| R4 | The app starts the display reader beside itself and gives any window or announcer a display's drawn text: characters, position, font, colour, and whether the frame is complete. An incomplete frame is never shown or spoken as a page. | 02 (the reader program itself: the private reference) |
| R5 | When the reader cannot read (it is not installed, the simulator version is not recognised, no access), BA says so once, in the pilot's words, and every window shows why. | 02 |
| R6 | The MCDU window shows the page from the reader, both sides, in the app's Airbus MCDU format; no pop-out, camera change or focus change. The pixel reader, its glyph file and its training tool are removed. When the reader cannot read, the window says so and BA speaks a message: no AI read of the MCDU. | 03 |
| R7 | The scratchpad is confirmed from the MCDU's own text as it is typed; an FMS message is spoken with the window closed. | 03 |
| R8 | Every input event the simulator lists for the aircraft is either a panel control or listed as left out, with the reason; a test fails on an unaccounted one. | 04 |
| R9 | Every control's name and position names come from the aircraft's own strings, and each position's pairing with its value is proven live or listed as unproven. No invented control, panel or position. | 04 |
| R10 | Every control is driven and read back once (a sweep that never drives what cannot be undone); a dead, inverted or mislabeled control is fixed. | 04 |
| R11 | Panels keep the cockpit's own panels. Within a panel, what every flight uses comes first, in the order the simulator's own procedures first use it; then what is set once; then non-normal and maintenance controls. A test pins the order. | 04 |
| R12 | The FCU windows, the autopilot window and the altimeter window work in flight as on the ground; a knob turned on hardware is called out; the app's automation can engage and read the autopilot. | 05 |
| R13 | The First Officer has what the other Airbus profiles have, passes the tests that sweep every profile, follows the simulator's own procedures in order, and reports no write as done that it did not see take effect. | 06 |
| R14 | Every EFB page meets the EFB checklist (section 4), has captured fixtures and tests, and its connection closes on an aircraft switch. | 07 |
| R15 | Output Alt+E and Alt+S open live E/WD and lower ECAM windows with the ECAM keys; warnings, cautions and memos are called out, each kind on its own Ctrl+M row. | 08 |
| R16 | Output Alt+N, Alt+I and Ctrl+Shift+D open live ND, ISIS and DCDU windows with their controls; ND messages and DCDU uplinks are called out. | 09 |
| R17 | The FMA's words are the PFD's own: each mode number's word is taken from what the PFD drew while that number was set. Alt+P opens a PFD window if the PFD proves readable; if not, the plan says so and the AI read stays. | 09 |
| R18 | The missing hotkeys and definition members of section 2.6 exist; everything spoken has a comma-decimal test. | 10 |
| R19 | One flight, gate to gate, with the First Officer, proves what only a flight can (section 6). | 11 |
| R20 | The temporary chocks and covers press is removed before the pull request; the three rows become status lines. | 12 |
| R21 | A release carries the reader program beside the app. The guide, the hotkey guide, the developer doc, the rules and one `aircraft` changelog fragment say what a pilot now has; `docs/a330/` is deleted; one pull request. | 12 |

## 4. The team's rules

**The simulator.**
- A task marked [sim] needs the A330 loaded on the ground; [in flight] needs a flight; [owner] needs the
  owner's ears, hands or say-so. Never a step that needs sight: measure instead.
- Reads are always fine. Never write to the simulator without its owner's go-ahead. Put back what a test
  changed, and say what could not be put back.
- Never load a flight, pause or move the aircraft for a test unless the simulator's owner asks.
- Never drive the owner's desktop (keys, mouse, focus). Cockpit keys go through the simconnect MCP's
  calculator path in `execute` mode (never a write in `read` mode); screens are read through the reader
  or the Coherent debugger with MSFSBA closed (one inspector connection a view, [DBG-9]).

**The display reader.**
- The reader is a program built from the private reference. Nothing in this repository reads the
  simulator's memory for the A330, and no public text (code, comments, tests, docs, commit messages, the
  pull request) says how the reader works: it speaks of "the display reader" and "frames".
- The app talks to it only through `IA330Displays` (plan 02). A fixture is a frame in that contract's
  form: drawn text, positions, fonts, colours, and nothing else.
- The aircraft's package is never decrypted. What a pilot types (a SimBrief ID, a logon code, a route) is
  never logged or put in a fixture.

**Sources for names and order.** A control's name, its position names, a procedure's items and their
order come only from the aircraft's own strings, what the simulator shows (its checklist window, its
tooltips), and what was measured live. The private reference reads the aircraft's strings and hands this
repository two small data files: the controls' names and position names, and the order of BA's own
controls and flow steps. The simulator's checklist text itself stays private. Never from another aircraft, another
add-on or memory of the real aeroplane. A real-aeroplane fact used to explain an order is cited with its
source in the plan's evidence file.

**The EFB checklist** (what "compliant" means; the app's rules in brackets):
1. The window is the shared EFB shell; the native list is only its fallback ([FPD-2]).
2. Rows are keyed on content with state suffixes stripped, and updated in place ([FPD-3] to [FPD-5]).
3. Only a control that does something is a button; status is text ([FPD-6], [INI-9]).
4. A typed value is committed the way the page accepts it, proven per field, and read back ([FPD-8]).
5. A disabled control stays reachable and says "Unavailable" ([FPD-10]).
6. Notices are read from the page's notice list; errors are found by structure ([FPD-11], [FPD-12]).
7. A press is confirmed only where the label carries the new state ([MD11-26]); never twice ([CORE-7]).
8. The current page and selected options are marked "(current page)" and "(selected)".
9. Units come from the page's own unit setting ([PEFB-7]).
10. Alerts are said at once; no button or glyph is nameless ([PEFB-2], [PEFB-3], [PEFB-6]).
11. A credential field is masked, and its value is never spoken, logged or captured.
12. One connection a view, closed when the aircraft goes ([DBG-9]).
13. Each page has a captured fixture and the agent's tests run on it.

## 5. Decisions taken (each is the owner's to reverse)

- D1. The displays are read by the display reader of section 2.1 (owner, 2026-11-19: approved).
- D2. The reader itself (its code, its plan, the notes and probe scripts, and anything that reads the
  aircraft's files) lives in the private A330 reference, never in this repository: "We want to keep the
  methods of how displays are read, or the actual code private" (owner, 2026-11-19). It ships as a built
  program beside the app; a release builds it from the private reference, as it fetches the display
  layouts today.
- D3. When the reader cannot read, the MCDU window says so in one status row and BA speaks a message.
  There is no AI read of the MCDU: "the mcdu shouldn't use an AI read once done as it's just too complex,
  instead should speak a message" (owner, 2026-11-19). No pixel matcher is kept. The other display keys
  (E/WD, system display, PFD, ND), which are AI reads today, fall back to the AI read without a word.
- D4. The MCDU window speaks no colour words, as the app's other Airbus MCDU windows; a greyed prompt
  keeps "(unavailable)", an amber box row reads as empty and required. (Claude, from the Fenix and
  FlyByWire windows; plan 03 records the wording.)
- D5. The first officer's MCDU, EFIS panel, radio panel and audio panel are offered, after the
  captain's. (Claude, sighted parity.)
- D6. The First Officer gets automatic flaps if they can be done on this aircraft (owner, 2026-11-20),
  built as the FlyByWire profiles' schedule is ([FO-15]) and behind the same "Auto-manage flaps" setting.
  It needs the flap lever's write and read measured on this aircraft and its F, S and green dot speeds
  read in flight; if one of them cannot be had, plan 06 records which and the setting stays hidden for
  this aircraft.
- D13. The controls' names and position names and the aircraft's variable names are held in this
  repository, as for the other aircraft (owner, 2026-11-20). The simulator's checklist text and anything
  about how the displays are read stay private (D2).
- D7. The FMA stays spoken from the mode L:vars, which change at once; the reader supplies and checks the
  words (R17). (Claude.)
- D8. Which display says a colour follows the Fenix's windows, display by display (owner, 2026-11-20:
  "some displays don't need them ... You can use the fenix as a reference"). A colour is said only where
  it is the meaning; it is never guessed, and always the word for what the display drew.

  | Display | Colour words | The Fenix rule it follows |
  | --- | --- | --- |
  | MCDU | None. A greyed prompt reads "(unavailable)"; an amber box row reads as empty and required (D4) | the Fenix and FlyByWire MCDU windows |
  | DCDU | None | the Fenix DCDU window (the owner's ruling for it, 2026-11-03) |
  | E/WD | A message line (warning, caution, memo, action line) says its colour after its words ("ENG 1 FIRE, red"; "PARK BRK, green"), each stretch its own when a line has several. A value in the engine block says a colour only when it is not its normal one ("XX amber") | [FNX-24], [FNX-43] |
  | Lower ECAM | A value says a colour only when the page draws it in other than its normal one (amber, red). Titles, labels and units: none | [FNX-37] |
  | PFD | The flight mode band's words say their colour ("Vertical: ALT, cyan": green engaged, cyan armed), in the window and in the flight mode call-outs. Nothing else on the PFD: no colour on the tapes, the values or the flags | the Fenix FMA speech and PFD window |
  | ND | Amber and red always. Any other colour only where it is unusual for that kind of item, or tells two items apart | [FNX-41] |
  | ISIS | None | the Fenix ISIS window |

  "Normal" is measured per display from the captures, never assumed: green for a value, white for a
  label, cyan for a unit on the two ECAM displays.
- D10. The MCDU window slews key for key, as the FlyByWire A320 and Fenix windows: Page Up and Alt+Up
  press the MCDU's up-arrow key, Page Down and Alt+Down its down-arrow key (owner, 2026-11-20: "Match
  the 20"; done that day).
- D11. The tablet's weight unit is the pilot's setting. BA says the planned weights with the tablet's
  unit and warns when the planned total is over the tablet's own maximum; it never converts a weight or
  changes the unit (owner, 2026-11-20: "something to note not something that you have to do").
- D12. The route comes from the MCDU's own request on the INIT page, after the tablet's Dashboard
  import; the guide teaches that order (the owner's own preflight, 2026-11-20). BA types no route for
  the pilot ([FO-2]).
- D9. Each display window carries, under its live list, the cockpit controls that change that display,
  "similar to the 757 and Fenix approach" (owner, 2026-11-19): the E/WD window the ECAM control panel's
  keys, the lower ECAM window the page keys, the ND window the EFIS panel's mode, range, option keys and
  needle selectors, and likewise the PFD, ISIS and DCDU windows. A window's press is the panel's own
  press, never a write of its own, and each button shows the control's state.
**Questions for the owner** (answer in this table; a plan proceeds on the decision above until then):

| # | Question | Answer |
| --- | --- | --- |
| Q2 | D6: no automatic flaps for the A330's First Officer? | "The FO can get automatic flaps if that's possible" (2026-11-20): D6 now says so. |
| Q4 | The controls' names and position names come from the aircraft's own tooltips (short placard words, as the other aircraft's maps carry). Keep those in this repository, or hold them back too? | "control names and lvars can stay like other planes" (2026-11-20): they are held in this repository; D13. |

## 6. Measurements

Measured on 2026-11-20 in one ground session (cold and dark to a programmed MCDU; the evidence and the
captured frames are with the team's private reference). "Open" is what is still to measure.

| # | What | Plan | Measured 2026-11-20 | Open |
| --- | --- | --- | --- | --- |
| M1 | The reader's time to a complete page on each display (INIT, F-PLN with a route, a long list), its processor load, and the cost to the simulator's frame time | the reader's plan (private); 02 sets the refresh defaults from it | With the slow proof: MCDU pages 0.5 to 3 s (the 804-character ARRIVAL list 1.5 s), E/WD 2 to 9 s, lower ECAM pages 1.5 to 6 s | The built reader's times, its processor load, the simulator's frame time |
| M2 | The reader with the simulator paused, in the menus, during a flight load, after an aircraft change, with the MCDU dimmed to zero and with the aircraft unpowered | the reader's plan (private); 02 Task 6 | Paused: reads as usual. MCDU dimmed to zero: reads as usual. Cold and dark: nothing is drawn, so nothing is read | The menus, a flight load, an aircraft change |
| M3 | Every special character the MCDU fonts draw and its meaning | 03 | Done: the table is in plan 03 | |
| M4 | MCDU keys never verified: OVERFLY, DECIMAL, SPACE, PLUS/MINUS, CLR on a message and on the scratchpad | 03 | Done, all work: the results are in plan 03 | |
| M5 | The 649 input events, and for each named control the value each position name belongs to | 04 | | All of it |
| M6 | The order of every procedure in the simulator's checklist window | 04, 06 | Done: 38 procedures, 669 items, captured in order from MSFS 2024's own tablet (kept private) | Turning it into `procedure_order.json` (the app's control keys in that order) |
| M7 | A/THR, AP 1 and 2, LOC, APPR presses in the air; Standard in a climb and on the first officer's knob | 05, 11 | Standard on the first officer's knob, on the ground: works by value as the captain's | The presses in the air; Standard in a climb |
| M8 | The engine start and shutdown flows; the ADIRS, flap, spoiler and parking brake writes | 06, 11 | Parking brake: input 0 = set, 1 = released. ADIRS mode by value: 1 = NAV, all three aligned | The flows; flaps and spoilers |
| M9 | Whether the PFD can be read: how often the reader gives a complete PFD frame within 2 s | 09 | The proof run eight times side by side read 30 of 31 items within 2.5 s; one alone 24 of 30 in 20 s. The flight modes are text | The built reader's figure: the go or no-go stands |
| M10 | Each FMA mode number against the words the PFD draws, through a whole flight | 09, 11 | At the gate, flight director on: ALT and NAV armed, in cyan | The flight |
| M11 | The DCDU with a real uplink; the ND with a route, in each mode | 09, 11 | The ND with a route read complete at 10 NM (16 items) and was NOT complete at 20 and 80 NM (at best 7 of 13 items in 10 s, eight side by side); without a route it is complete in 1 s | Why the ND with a route is hard, with the built reader: a second go or no-go in plan 09. The DCDU |
| M12 | The visual-guidance glideslope and flare biases, measured apart on an approach; the taxi turn lead on a taxi | 10, 11 | | All of it |
| M13 | Whether the aircraft publishes its flight phase, and its values through a flight | 10, 11 | Yes: `INI_flight_phase`, `INI_EWD_FLIGHT_PHASE`, `INI_ECAM_PHASE`, each 1 at the gate | Their values through a flight |
