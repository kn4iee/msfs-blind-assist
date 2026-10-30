---
title: "Airbus Aircraft"
---

<div class="guide-part" markdown="1">

Chapter 1 of 2 in [Part 8: Aircraft Guides](index.md#part-7-aircraft-guides) of the MSFS Blind Assist user guide.

</div>

<nav class="chapter-toc" aria-labelledby="in-this-chapter" markdown="1">

<p id="in-this-chapter"><strong>In this chapter:</strong></p>

- [FlyByWire A320neo (A32NX)](#airbus-a32nx)
- [Fenix A320 CEO](#airbus-fenix)
- [FlyByWire A380X](#airbus-a380)
- [Headwind A330-900neo](#airbus-a330)
- [iniBuilds A350](#airbus-a350)

</nav>

BA supports five Airbus aircraft: the FlyByWire A320neo (often called the A32NX), the Fenix A320 CEO, the FlyByWire A380X, the Headwind A330-900neo and the iniBuilds A350. Three of them come from the same family of code. The Headwind A330 is built on top of the FlyByWire A320, and the A380X is made by the same FlyByWire team, so once you have learned one of these three, the others will feel familiar. The Fenix is a separate product with its own way of working, and BA treats it differently in a few places. The iniBuilds A350 is separate too, but BA gives it the A380's panel names, windows and keys.

A few things are true of all five:

- BA does not switch profiles by itself: choose the aircraft from the Aircraft menu before you fly (see [Choosing Your Aircraft](getting-started.md#gs-aircraft)).
- They all need the free MobiFlight WASM module to operate switches and knobs (see [What You Need](getting-started.md#gs-requirements)).
- The FCU windows work as described in [Autopilot Windows](takeoff-climb-and-cruise.md#autopilot-windows); this chapter covers the Airbus details.
- On the A32NX, the Headwind A330 and the Fenix, the MCDU line select keys are Ctrl+1 to Ctrl+6 (left) and Alt+1 to Alt+6 (right), or F1 to F12 with the alternate setting in [FMC Settings](planning-your-flight.md#planning-fmc-settings). The A380's and the A350's MFDs have no line select keys; you arrow to a line and press Enter instead (see their sections).
- Output Ctrl+M opens the aircraft's [Monitor Manager](core-concepts.md#monitor-manager).

## FlyByWire A320neo (A32NX) {#airbus-a32nx}

The FlyByWire A32NX is a free, highly detailed A320neo, and it is the most complete Airbus in BA. Almost every switch in the cockpit is reachable from the panels, the MCDU is fully readable, the flyPad tablet is a browsable document, and you can even answer ATC datalink messages.

### Before you fly {#before-you-fly}

You need:

- The FlyByWire A32NX, installed with the FlyByWire Installer.
- The MobiFlight WASM module in your Community folder.
- A screen reader.

You do not need SimBridge (FlyByWire's companion program) for normal flying. BA reads the MCDU straight from the aircraft's own display. SimBridge is only used for MCDU printouts, and as a backup if BA cannot reach the display directly. You do not need Developer Mode or any Community-folder add-on from BA.

The aircraft's displays must be powered (battery on, or a powered preset loaded) before there is anything to read.

### How the panels are organised {#how-the-panels-are-organised}

Press Ctrl+1 to reach the Sections list and Ctrl+2 to reach the Panels list. The A32NX has four sections.

**Overhead** holds the overhead panel, in this order:

- **ELEC**: batteries 1 and 2, external power (and whether it is available), generators 1 and 2, APU generator, bus tie, AC essential feed, IDG disconnects, commercial and galley power, and the emergency generator controls.
- **ADIRS**: the three IR mode selectors (Off, Nav, Att), and the IR and ADR pushbuttons.
- **APU**: APU master switch, APU start, and the auto-extinguish test and reset.
- **Oxygen**: crew supply, passenger masks, the passenger oxygen light and the timer reset.
- **Fire**: guards and handles for both engines and the APU, the fire tests, and the agent discharge buttons.
- **Hydraulics**: engine and electric pumps with their fault lights, the PTU, the blue pump override, leak measurement valves and the RAT manual deploy.
- **Fuel**: the mode selector, the six tank pumps and the crossfeed.
- **Air Conditioning**: cockpit, forward cabin and aft cabin temperatures (type a value in degrees Celsius, 18 to 30), and hot air.
- **Bleed**: APU bleed, engine bleeds, cross bleed, pack flow, packs 1 and 2 and ram air.
- **Pressurization**: mode, manual cabin vertical speed, ditching and landing elevation.
- **Ventilation**, **Anti Ice** (wing, both engines, probe and window heat), **Wipers**, **Signs** (seat belts, no smoking, emergency exit lights).
- **Interior Lighting** (annunciator lights, dome, and flood and integral brightness levels from 0 to 100) and **Exterior Lighting** (nose, landing, strobe, beacon, wing, nav and logo, runway turn-off, plus "all landing lights on" and "all off").
- **Calls**, **GPWS**, **Flight Control Computers** (ELACs, SECs, FACs and rudder trim), **Cockpit** (cockpit door, door video, armrests, third occupant seat), **Evacuation**, **Cargo Smoke**, **Recorder and Misc**, and **Engine Start** (the FADEC ground power switches).

**Glareshield** holds:

- **FCU**: heading, speed, altitude and vertical speed values with their push and pull knobs, LOC, APPR, Expedite, autopilots 1 and 2, autothrust, the red AP disconnect, SPD/MACH and the TRK/FPA toggle.
- **EFIS Captain** and **EFIS First Officer**: ND mode and range, navaid selectors, the ILS button, the flight director, the altimeter (value, inHg/hPa unit, push and pull for QNH and standard), and the map filters (constraints, waypoints, VOR/DME, NDB, airports).
- **Warnings**: the master warning and master caution acknowledge buttons for both sides.

**Instrument** holds the main instrument panel:

- **Gear**, **Autobrake** (DIS, LO, MED, MAX, plus the brake fan), **ISIS**, **Source Switching** and **Clock** (chronometer start and stop, reset, and elapsed time).
- **PFD**, **ND**, **ISIS**, **System Display** and **Status**, which are mostly read-only status displays. This is where you read the flight instruments; see "Special windows" below.

**Pedestal** holds:

- **Flight Controls** (spoilers armed status, speed brake handle, flaps), **Speed Brake** (a combo, arm and disarm, retract, full, and a fine control), **Parking Brake**, **Engines** (engine mode selector and both engine masters), and **Thrust Levers** (set all levers, or each lever, to a detent).
- **ECAM Control Panel**: every page key (ENG, APU, BLEED, COND, ELEC, HYD, FUEL, PRESS, DOOR, BRAKES, FLT CTL), ALL, STS, RCL, T.O. CONF, EMER CANC and both CLR keys.
- **Weather Radar**, **Transponder** (mode, ATC system, altitude reporting, squawk, ident, TCAS mode and display, and the ATC message acknowledge) and **RMP**.

The **RMP** panel is where you tune the COM radios. For COM 1, 2 and 3 there is a **Standby Frequency** field, an **Active Frequency** field and an **XFER** (swap) button, followed by the RMP power and mode switches. Type a frequency such as 121.500 and press Enter. BA then announces the result, for example *"COM 1 active 121.500"*, and after a swap you hear both the new active and the new standby frequency.

There is no Doors or Ground Services panel on the A32NX. Door states are announced automatically as they change (*"Forward Left Door open"*, for example), and ground services such as the jet bridge, stairs, catering and fuel truck are handled on the flyPad's Ground page.

### Autopilot / FCU windows {#autopilot-fcu-windows}

In Input mode, each of these opens a small window with a value box, the knob's push and pull buttons, and the relevant mode toggle. Type a value in the box and press Enter, and BA confirms the value it sent, for example *"FCU heading 270, selected"*. In the altimeter window (Ctrl+B), Enter applies only from the value box; choosing a mode or a unit in its lists applies at once.

<div class="table-wrap" markdown="1">

| Key | Window |
|---|---|
| Input Ctrl+S | Speed: 100 to 399 knots or Mach 0.10 to 0.99, Speed Push (managed), Speed Pull (selected), SPD / MACH toggle |
| Input Ctrl+H | Heading: 0 to 360, Heading Push and Pull, HDG·V/S / TRK·FPA toggle |
| Input Ctrl+A | Altitude: 100 to 49,000 feet, Altitude Push (managed) and Pull (selected), Increment 100 and Increment 1000 |
| Input Ctrl+V | Vertical speed (plus or minus 6000 feet per minute) or flight path angle (plus or minus 9.9 degrees), V/S Pull (engage), V/S Push (level off) |
| Input Ctrl+P | Autopilot panel: AP 1, AP 2, LOC, APPR, EXPED, A/THR, AP disconnect, A/THR disconnect, and a Flight Director status line |
| Input Ctrl+B | Altimeter: Mode (QNH or STD), Unit (hPa or inHg), and one value that sets both sides |

</div>

The buttons in the Autopilot panel always show their live state, for example "AP 1 (on)" or "LOC (off)". The A/THR button says whether autothrust is Disengaged, Armed or Active. The altimeter window confirms with *"Altimeter set to 1013 hectopascals, both sides"*.

The engage shortcuts, the push and pull keys and the Output Shift+H, Shift+S, Shift+A and Shift+V readouts are described in [Autopilot Windows](takeoff-climb-and-cruise.md#autopilot-windows). Turning an FCU knob on hardware or in the cockpit speaks the new value as it settles.

### MCDU {#mcdu}

Open the MCDU with Input Shift+M. The window shows the screen as a list, one line at a time. It is the Captain's MCDU. The aircraft draws both MCDU screens from one instrument, so there is no separate First Officer MCDU to switch to.

To read and move around:

- Up and Down arrow read the screen line by line. Labels and values keep their left, centre and right positions, so on a braille display the right-hand values line up in one column.
- Green text (usually the active choice, or a value the FMS has accepted) is marked with a star in front of the value, so "\*10.2/0213" means that value is shown in green.
- PageUp and PageDown scroll (the flight plan, and runway lists on the departure and arrival pages). Left and Right arrow move between pages of a multi-page form such as PERF when the display list has focus. Alt+Left and Alt+Right do the same from anywhere in the window, and Alt+Up and Alt+Down scroll.

To type into the scratchpad:

1. Press Alt+S to jump to the scratchpad input field.
2. Type your entry, for example a waypoint name or a cruise level.
3. Press Enter. BA sends it to the MCDU scratchpad.
4. Press Alt+Home to return to the display, arrow to the line you want, and press its line select key.

Backspace clears the scratchpad.

The line select keys are Ctrl+1 to Ctrl+6 for the left side and Alt+1 to Alt+6 for the right side (or F1 to F12 with the alternate setting). The page keys are all on Alt:

<div class="table-wrap" markdown="1">

| Key | Page | Key | Page |
|---|---|---|---|
| Alt+I | INIT | Alt+U | FUEL PRED |
| Alt+D | DIR | Alt+K | ATC COM |
| Alt+P | PROG | Alt+M | MCDU MENU |
| Alt+F | F-PLN | Alt+A | AIRPORT |
| Alt+E | PERF | Alt+T | DATA |
| Alt+R | RAD NAV | Alt+O | OVFLY |
| Alt+Shift+F | SEC F-PLN | Alt+C | CLR |

</div>

What you hear: the page title when the page changes, and the scratchpad when a message appears there, such as an FMS warning. Once you have opened the MCDU window, it keeps reading out FMS messages such as DEST EFOB BELOW MIN even after you close it, but a closed window does not announce page changes. If SimBridge is running, printouts are read too, starting with *"Printer:"*. The window's status line says "MCDU: Connected" or "MCDU: Disconnected".

### EFB {#efb}

The FlyByWire flyPad opens with Input Shift+T, in a window called "A320 flyPad EFB", and works as described in [Aircraft EFBs](planning-your-flight.md#aircraft-efb). The current page is shown as "(current page)" in the navigation, and selected Ground tabs and options read "(selected)". The flyPad is the same on the A32NX, the Headwind A330 and the A380X. Some highlights:

- **Dashboard**: your flight, weather, and SimBrief import (set your SimBrief username in the flyPad Settings first; see [SimBrief](planning-your-flight.md#simbrief)).
- **Ground**: Services (doors, jet bridge, stairs, GPU, baggage, catering, fuel truck), Fuel and Payload. Each door tile carries the door's real name and says whether it is active. Wheel chocks and safety cones read as status lines, "placed" or "not placed". They are information only: the aircraft places them itself.
- **Payload** and **Fuel**: BA rebuilds these pages as readable lines and fields. Each payload field tells you the current and maximum values and the unit, and the refuel target is offered both as a slider and as a number field.
- **Settings**: each setting reads its name followed by its choices, with the selected one marked. The "US Units" setting switches BA's own weight readouts (gross weight and fuel) between pounds and kilograms, so what you hear matches the aircraft.

<div class="callout note" role="note" markdown="1">

**Note:** The flyPad's Pushback tab has not been specially adapted for screen readers, so its controls may be hard to use. For pushback we recommend GSX; see [Gates, GSX and Docking](gates-gsx-and-docking.md).

</div>

### Special windows {#special-windows}

**DCDU (datalink): Input Ctrl+Shift+D.** The DCDU is the small screen below the glareshield where controller-pilot datalink (CPDLC) messages arrive. On the A32NX it is the only place you can answer them; the MCDU only shows the message history.

Before it will work, you need a datalink connection:

1. In the flyPad Settings, choose an ACARS provider: Hoppie, SayIntentions or BeyondATC.
2. On the MCDU, go to the ATC COM pages (Alt+K) and log on to ATC. The NOTIFY key needs the ATC flight number, the ATC centre, and a FROM/TO on the INIT page.

In the DCDU window, each soft key appears as a line starting with its key number, with an arrow showing the side. "1 &lt;STBY" is left key 1, and a line ending "RECALL&gt;" is a right key. The keys use the same chords as the MCDU: Ctrl+1 and Ctrl+2 for the left keys, Alt+1 and Alt+2 for the right keys (or F1, F2, F7 and F8 in alternate mode). Long messages are split into the unit's own lines, exactly as a sighted pilot sees them.

Answering is a two-step process, just like the real unit:

1. Read the message to the end. PageDown moves to the next page of a long message, PageUp goes back.
2. Press the answer key, for example Alt+2 for WILCO. The keys change to CANCEL and SEND.
3. Press Alt+2 again to send.
4. After sending, right key 2 becomes CLOSE, which dismisses the message.

Ctrl+PageUp and Ctrl+PageDown move to older and newer messages. F5 refreshes and Escape closes.

On a long message, the answer keys stay inactive until you have paged to the end. If you press one too early, you hear for example *"WILCO not available yet. Page 1 of 2. Press page down to read on."* If a key still will not work after you have read to the end, you may hear *"WILCO not available. The message may still be transmitting."* If the window cannot reach the DCDU it shows "DCDU unavailable. Retrying...".

**E/WD window: Output Alt+E.** This opens the engine and warning display as a window: the thrust rating and limit, each engine's N1, EGT, N2 and fuel flow, fuel on board, and the ECAM memo and warning lines. Read it with the arrow keys. It refreshes by itself; F5 refreshes it at once and Escape closes it.

**PFD, ND, ISIS and System Display.** These have no window of their own. Read them from the panels in the Instrument section: the **PFD** panel (flight mode annunciator, speeds, minimums), the **ND** panel, the **ISIS** standby instruments and the **Status** panel. Press Ctrl+3 to jump straight to the status display of the panel you are on. For the System Display, open the **System Display** panel and choose a page in its **System Display Page** combo: Upper E/WD, Electrical, Hydraulics, Pressurization, APU, Air Conditioning, Wheel / Brakes, Bleed, Fuel, Doors, Engine and Flight Controls. The status box then lists that page's values. Choosing a page here only changes what BA reads, not the page on the aircraft's screen.

**First Officer: Output Shift+C.** Flows and checklists from cold and dark to securing the aircraft. See [The First Officer and Checklists](first-officer.md).

### Aircraft-specific hotkeys and callouts {#aircraft-specific-hotkeys-and-callouts}

The FlyByWire Airbuses answer the full set of [Readout Hotkeys](core-concepts.md#readouts), including Shift+1 to Shift+6 (Green Dot, S, F, VLS, VS and VFE Next), Ctrl+W (the active ND waypoint) and Ctrl+0 (approach capability). Output Ctrl+E switches the automatic E/WD callouts on and off (*"E W D monitoring enabled"* or *"disabled"*). The rest are in the [Hotkey Reference](hotkey-reference.md).

BA announces these automatically:

- Flight mode annunciator changes: vertical, lateral and autothrust modes. An armed altitude that carries an FMS constraint is announced as *"Altitude constraint armed"*, so you can tell it from an ordinary armed altitude without reading the PFD.
- ECAM warnings, cautions and memos, and master warning and master caution.
- *"Mode reversion, check FMA"*, when the flight guidance has reverted to another mode by itself, and *"Altitude discrepancy between sides"* when the captain's and first officer's altitudes disagree (the CHECK ALT flag a sighted pilot sees on the PFD).
- Altimeter changes, approach minimums, flaps and gear.
- TCAS traffic and resolution advisories, including what to fly; see [TCAS Traffic](takeoff-climb-and-cruise.md#tcas-ra).
- COM frequency changes, and *"Transmitting on VHF 1"* when you start transmitting.
- Flight phase changes, such as *"Entering Climb phase"*.
- Door states, and aircraft preset loading progress (*"Aircraft preset loading complete"*).
- *"V1"*, *"Rotate"* and *"V2"* on the take-off roll, from the MCDU PERF TAKE OFF page; see [Takeoff Speed Callouts](takeoff-climb-and-cruise.md#takeoff-vspeed-callouts). Speeds the FMS has cleared are read as "not set".

Every one of these can be muted in the Monitor Manager (Output Ctrl+M).

### Things to know {#things-to-know}

- **Wing anti-ice, the evacuation horn and other one-way controls behave like the real ones.** For example, the evacuation horn shut-off silences the horn (*"Evacuation horn silenced"*) and is not something you switch back on.
- **RTO autobrake may refuse to arm right after a flight.** That is the aircraft's own logic, not a fault; it arms normally on a fresh start.
- **Weight units follow the flyPad's "US Units" setting**; you hear *"Weight units kilograms"* or *"Weight units pounds"*.
- **There is no metric altitude on the A320.** The real A320 has no MTRS button, so BA does not offer one.
- **The A32NX is updated often.** FlyByWire's development builds sometimes rename things underneath. If a readout suddenly says "not available" after an aircraft update, update BA too and report it if it persists.

## Fenix A320 CEO {#airbus-fenix}

The Fenix A320 is a payware A320 (the older "CEO" model) with a very deep systems simulation. Fenix v3 also flies as the A319 and the A321, with CFM or IAE engines, and the "Fenix A320 CEO" profile covers them all. BA gives you access to more than 300 switches and knobs, monitors over 470 lights, gauges and switch states, and reads both MCDUs. Its own Fenix windows (Input Ctrl+Shift+F) give you the Fenix's engine readings, the flight attendant panel, failures, documents and options. Because the Fenix draws its main displays in a way BA cannot read directly, the PFD, ND and ECAM screens are read with AI vision instead.

### Before you fly {#before-you-fly-2}

You need:

- Fenix v3, loaded and running in the simulator. Older Fenix versions (2.x) are no longer supported, so update the Fenix if you have not. The MCDU, the EFB and the Fenix windows all talk to the Fenix's own data service, which only runs while the aircraft is loaded.
- The MobiFlight WASM module in your Community folder. BA works the switches through it, and turns, pushes and pulls the FCU and baro knobs through it too.
- For reading the PFD, ND and ECAM displays: an AI provider set up with your own API key. See [AI Display Reading and Scene Description](core-concepts.md#ai-display-reading).
- For current navigation data: either your Navigraph account connected in the Fenix's own EFB, on the Settings app's Connections page (see [EFB](#efb-2) below), or a navigation data file you import yourself on the MCDU's DATA, A/C STATUS page.

About a second after the aircraft has loaded, BA says which Fenix it is, for example *"Fenix A321, IAE engines, 2 extra tanks"*. The panels then offer only the controls that aircraft has (see below).

### How the panels are organised {#how-the-panels-are-organised-2}

The Fenix has five sections. Some controls are only on some Fenix aircraft; the lists say which, and "Controls that depend on your Fenix", after the lists, says how BA handles them.

**Overhead**:

- **Electrical**: batteries, generators, APU generator, external power, APU master and start, bus tie, AC essential feed, IDGs, galley, commercial, and the emergency generator controls and covers.
- **ADIRS**: the three IR mode selectors and the ADR pushbuttons. On the GNADIRS panel it adds the data and system display selectors and the full keypad (keys 0 to 9, Clear and Enter), so you can enter a position; on the plain ADIRS panel it adds the IR pushbuttons instead.
- **Air Conditioning and Pressurization**: bleeds, packs, hot air, ram air, ditching, pressurization mode and manual vertical speed, ventilation, cross bleed, pack flow (Econ Flow, Off or On, on the A321), the three zone temperatures, and on aircraft with aft cargo heat, Aft Cargo Hot Air and Aft Cargo Temperature (0.0 to 1.0, like the zone temperatures).
- **Fire**, **Hydraulic**, **Fuel** (six tank pumps, crossfeed, mode selector, and ACT Pump, Off or On, on aircraft with extra center tanks), **Anti-Ice**, **Signs**, **Exterior Lights**, **Interior Lights**, **Voice Recorder**, **Oxygen** (including mask tests), **Evacuation**, **Calls** (with Calls Mid and Calls Exit on the A321), **Wipers** (including rain repellent), **Cargo Smoke**, **GPWS**, **Flight Control Computers** (ELAC 1 and 2, SEC 1 to 3, FAC 1 and 2), **Engine** (manual start, and on IAE engines the N1 mode switches) and **Maintenance** (FADEC ground power, ELT, APU auto-extinguish test and reset, and more).

**Pedestal**:

- **Engines** (engine mode and both masters), **Weather Radar**, and **ECAM**: TO CONFIG, every page key, ALL, STS, both CLR keys, RCL, EMER CANC and the ECAM brightness controls.
- **DCDU Left**, on liveries with a DCDU: brightness and the DCDU's keys. You can press them, but BA cannot read the DCDU screen on the Fenix.
- **Flight Controls**: parking brake, speed brake, rudder trim (left, right and reset), elevator trim position, the flap lever, the thrust levers (left, right or both) and the A/THR and AP disconnect buttons for both sides.
- **ATC TCAS**: TCAS and transponder modes, altitude reporting, traffic display, ident and the squawk code.
- **Radio Management Panel (RMP)**: COM 1, 2 and 3 standby frequency and transfer, and every RMP's power and mode buttons (VHF, HF, NAV, VOR, ILS, GLS, ADF, BFO and AM).
- **Audio Control Panel (ACP)**: the captain's volume knobs, transmit keys, reception switches (VHF 1 to 3, HF 1 and 2, interphone, cabin, VOR 1 and 2, marker and others), RESET, VOICE and the INT/RAD switch.

**Main Instrument Panel**: **Auto Brakes** (Low, Medium, Max, brake fan), **Landing Gear** (gear lever and anti-skid), **Clock** (see below), **Switching**, **Console Floor Lights**, **ISIS** (on aircraft with the digital ISIS: its buttons, its baro knob and ISIS Baro Push, the knob's push), **GPWS/Terrain**, **Warnings/Messages** (master warning, master caution, ATC message and chronometer buttons for both sides), **Autoland**, **Main Instrument Lights** and **Audio** (loudspeakers).

**Glareshield**: **EFIS Left**, **EFIS Right** (ND mode and range, the baro knob's Pull (STD) and Push (QNH), baro mode and adjust, navaid selectors, map filters, flight director, LS) and **FCU** (every FCU button and the four knobs, each with decrease, increase, push and pull). The flight director and LS buttons read their state in their names, for example *"EFIS Left LS: On"*.

**Cockpit**: **Cockpit Door** (see below), **Captain Seat** and **First Officer Seat** (height, distance and armrests), **Windows and Shades**, **Standby Instruments** (the standby compass, and the attitude cage on aircraft with the round standby gauges) and **Cockpit Other** (Headset first, then the jumpseat and its headrest, the oxygen mask covers, gravity gear extension, and on liveries with a DCDU the First Officer's DCDU brightness).

**Cockpit Door** holds everything about the door: **Cockpit Door** (Closed or Open; the door the First Officer closes before start and opens at shutdown), **Overhead Video** (the overhead VIDEO pushbutton) and **Pedestal Video** (the video button on the pedestal). As in the Fenix's own cockpit, the door does not open while the jumpseat is out, and the jumpseat does not deploy while the door is open: you hear *"Stow the jumpseat first."* or *"Close the cockpit door first."* The door says when it moves, whoever moves it, for example *"Cockpit Door: Open"*.

**Gravity Gear Extension** (Cockpit Other) is one control, Stowed or Extended. Choose Extended and BA raises the handle and turns the crank three times, which takes about six seconds, then says *"Gravity gear: cranked 3 turns."* Choose Stowed and it turns the crank back and stows the handle: *"Gravity gear: crank stowed."* The control keeps your choice while the crank turns; a second choice before it finishes is refused with *"Gravity gear: crank already turning."*

**Clock** (Main Instrument Panel): Chronometer Start/Stop, Chronometer Reset, Elapsed Time (Run, Stop, Reset), UTC Selector (GPS, Internal, Set) and Date. Its status display reads the chronometer on the "CLOCK CHRONO" line ("2 minutes 5 seconds") and the elapsed time on the "CLOCK ELAPSED" line ("1 hours 12 minutes"), each "Reset" when blank. Elapsed Time springs back from Reset to Stop by itself, about half a second after you choose Reset. The UTC Selector's Set position does nothing yet, because the clock's SET knob, which sets the time and date with it, is not in BA.

**Headset** (Cockpit Other, Off or On): On puts the headset on, and the Fenix's noise cancelling quiets the cockpit sound.

**Controls that depend on your Fenix.** BA offers each Fenix aircraft its own controls:

- On the A321: Econ Flow instead of Pack Flow, and Calls Mid and Calls Exit.
- With extra center tanks: ACT Pump, whose upper and lower lights are announced.
- With IAE engines: the N1 mode switches, whose lights are announced (*"Engine N1 Mode 1 Lower: On"*). CFM engines have no N1 mode switches.
- On the GNADIRS panel: the ADIRS keypad and the data and system display selectors. On the plain ADIRS panel: the IR pushbuttons.
- On liveries with a DCDU: the DCDU controls. With the digital ISIS: the ISIS controls. With the round standby gauges: the standby attitude cage. With aft cargo heat: Aft Cargo Hot Air and Aft Cargo Temperature.

A control your aircraft does not have is left out of its panel. A panel whose controls your aircraft has none of is not listed at all, for example DCDU Left on a livery without a DCDU, or ISIS with the round standby gauges. Until BA knows which Fenix you are flying, it shows everything: a section you opened before then lists every panel until you move to another section and back, and a panel you opened before then shows every control until you open it again. Pressing a control your aircraft does not have says *"Not fitted on this aircraft."*

A note on the seats: the height and distance controls are three-position switches, Down, Stop and Up (or Aft, Stop and Forward). Choose Up and the seat keeps moving until it reaches its limit, then the switch returns to Stop by itself. The combo follows that, but quietly; seat movement is not announced by default.

Cargo Discharge 1 and 2 (Cargo Smoke panel) and the DCDU brightness switches spring back to the middle like the real ones: a moment after you choose an end, the control shows Rest (or Mid) again, so you can choose the same end again. DCDU brightness moves one step each time.

### Autopilot / FCU windows {#autopilot-fcu-windows-2}

The Fenix has its own set of FCU windows, opened in Input mode:

<div class="table-wrap" markdown="1">

| Key | Window |
|---|---|
| Input Ctrl+S | Set FCU Speed: 100 to 399 knots or Mach 0.1 to 0.99, Speed Push, Speed Pull, SPD/MACH |
| Input Ctrl+H | Set FCU heading: 0 to 360, Heading Push, Heading Pull, HDG/VS TRK/FPA |
| Input Ctrl+A | Set FCU altitude: 100 to 49,000 feet, Altitude Push, Altitude Pull |
| Input Ctrl+V | Set FCU vertical speed: minus 6000 to 6000, V/S Push, V/S Pull |
| Input Ctrl+P | Autopilot window: AP1, AP2, ATHR, APPR, LOC, EXPED, and the A/THR and AP disconnect buttons for each side |
| Input Ctrl+B | Set Altimeter: Mode (QNH or STD), Unit (QNH (hPa) or Inches (inHg)) and value |

</div>

In each window, type the value and press Enter; in the altimeter window, only Enter in the value box applies it. The Fenix's FCU and baro knobs are rotary encoders, so BA turns them click by click towards your value, counting from where the knob really is, even after you have turned it in the cockpit. A big change can take a moment to finish. With the Fenix's Encoder Acceleration option on, BA turns the heading, speed, altitude and vertical speed knobs in small steps and checks the value after each one, so a big change takes a few seconds; the altimeter is not affected. You hear only where a set ends: the new FCU value once, as when you turn the knob yourself, or, if a set stops anywhere but the value you typed, a sentence such as *"Altitude set to 7100, not 7000."* Altitudes are set in steps of 100 feet; if you type 4550, BA says *"Rounding to 4500 feet"*. A vertical speed goes to the 100 feet per minute step toward zero without a word: 1050 sets 1000, minus 1050 sets minus 1000. If the V/S window shows dashes (a managed climb or descent) and still does after BA's first click, BA stops and says, for example, *"Vertical speed still dashed, not -1000."* If the knob is already where you asked, you hear for example *"Already at heading 270"*. If you press Enter in the altimeter window while the previous setting is still being dialed in, BA says *"Altimeter set in progress, press Enter again when it finishes"* and keeps the first value.

The altimeter window sets both sides. A typed value always ends in QNH, and choosing QNH or STD in its Mode list pushes or pulls both baro knobs. When the setting is done, BA reads both sides back: *"Both altimeters set: 1015, 29.97."* when both are set to the same QNH, or each side by name otherwise (a side still in STD included), for example *"Captain altimeter 1015, 29.97. First officer altimeter still in STD."*

The FCU and baro knobs and their push and pull all go through the MobiFlight WASM module, so they need it installed. The push and pull keys and the Output FCU readouts work as on the FlyByWire aircraft.

### MCDU {#mcdu-2}

Open the MCDU with Input Shift+M. The Fenix has two MCDUs and BA reads both. A selector at the top chooses between "Left (Captain)" and "Right (First Officer)", and you can switch from anywhere in the window with Ctrl+Shift+L (left) and Ctrl+Shift+R (right). You hear *"Left MCDU"* or *"Right MCDU"*, then the page title. Each MCDU keeps its own page.

Reading, typing, the line select keys and the page keys all work as in the [A32NX MCDU](#airbus-a32nx), except that there is no Alt+C key (Backspace clears the scratchpad) and Left and Right switch between page sections, such as INIT A and INIT B.

The selected option in a group is marked with a star. For example, on the failures page "←NONE/MINOR/\*ALL" means ALL is selected. The window announces *"MCDU connected"* or *"MCDU disconnected"* when the connection changes.

### EFB {#efb-2}

The Fenix tablet opens with Input Shift+T (in Output mode, Shift+T toggles trim announcements instead), in a window called "Fenix EFB". On its own, the Fenix v3 tablet cannot be used with a screen reader, so BA adds what it lacks: every app is a button you can Tab to, every switch, box and list says its name, and selections and open chapters say their state. Read it in browse mode, like a web page; the window itself is described in [The Fenix and iFly EFBs](planning-your-flight.md#the-fenix-and-ifly-efbs).

- **Getting around:** Alt+Home goes back to the EFB's home screen from anywhere, even from a text box. In an app, the "App switcher" button opens the list of apps.
- **Takeoff and Landing:** each result reads its label right before its value, for example "V1" and then "142"; a result not worked out yet says "not computed". Landing's runway condition is one Tab stop where the arrow keys choose the code, and each system's page of faults has a "Back" button, with the faults as check boxes.
- **Failures:** a failure says ", failed" or ", armed", and a chapter with an active failure says ", has failures".
- **Settings:** every on/off option is a switch named by its setting. Each "More options" button opens its menu with the focus inside it, read right after the button; Escape or the "Close menu" button closes it. Your Navigraph account, one way for Fenix v3 to get current navigation data, is connected on the Connections page; the other is importing a navigation data file on the MCDU's DATA, A/C STATUS page.
- **Quick settings** has a "Close quick settings" button.
- **Escape** first closes an open menu, drop-down list or the Panel States pop-up; the next Escape closes the window.
- **The MCDU and FAP pages** cannot be read with a screen reader. If you open one, BA takes you back to the home screen and tells you where to go instead: *"The EFB's MCDU can't be read with a screen reader. Use the MCDU window instead: input mode, then Shift+M."* or *"The EFB's FAP can't be read with a screen reader. Use the Fenix windows list instead: input mode, then Control+Shift+F."*
- **Charts:** the map is pictures only; it is announced as "Moving map".

For documents, failures and options, the Fenix windows below are quicker: they search when you press Enter, open the pinned ECAM procedure with one key, and tell you what the Fenix did.

### Special windows {#special-windows-2}

**AI display reading.** The Fenix's PFD, ND, ECAM and standby displays are read with AI vision, in Output mode:

<div class="table-wrap" markdown="1">

| Key | Display |
|---|---|
| Alt+P | PFD |
| Alt+N | Navigation display (the captain's) |
| Alt+E | E/WD (engine and warning display) |
| Alt+S | SD (system display) |
| Alt+I | Standby instruments |
| Alt+D | Describe the outside scene |

</div>

BA moves the camera for you, as described in [AI Display Reading](core-concepts.md#ai-display-reading). These reads need Fenix v3: on an older Fenix the camera frames the wrong displays, so update the Fenix. Three details: Alt+N reads the captain's navigation display, the one whose range and mode you set on the left EFIS. Alt+P reads the First Officer's PFD; the two PFDs differ only in the altimeter setting and in which side's autopilot and flight director are shown. Alt+I knows whether your Fenix has the digital ISIS or the three round standby gauges. For exact engine numbers, use the Engines window below; Alt+E is the way to hear the E/WD's memos and warnings.

**Fenix windows: Input Ctrl+Shift+F.** This opens a list called "Fenix windows". Each row gives a window's name and what it holds, for example "Engines. N1, EGT, N2, fuel flow, oil and air temperatures, from the Fenix's own values". Arrow to a row and press Enter: the list closes and that window opens. Escape closes the list. The five windows, described below, are Engines, Cabin, Failures, Fenix documents and Fenix options. They read the Fenix's own data service, so the Fenix must be loaded. None of them speaks when it opens, each has a read-only status box you can Tab to, and they all close when you change aircraft. On any other aircraft, Input Ctrl+Shift+F says *"This aircraft has no extra windows."*

**Engines window.** The Fenix's own engine readings, in a window called "Fenix engines": the same numbers its screens show, where the simulator's own engine values do not match the Fenix. The list has one value per line, with the two engines on neighboring lines: first whether each engine is running ("Engine 1: running", "Engine 2: not running"), then each engine's N1, EGT, N2, fuel flow, oil quantity, oil pressure and oil temperature ("Engine 1 N1: 22.4 percent"), then the total and outside air temperatures. Fuel flow is in the weight unit set in the Fenix EFB, kilograms or pounds per hour; oil quantity is in quarts and pressure in psi. A value the Fenix does not give reads "--". The values update every second without moving your place. F5 reads them all again and says *"Engines refreshed"*; if the Fenix gives nothing, you hear *"No engine data from the Fenix. Is the Fenix loaded?"* While the window is open, BA says *"Engine 1 running"* or *"Engine 1 stopped"* when the Fenix's own engine state changes; "running" comes when the start is complete, not at light-up. The status box names your Fenix and the fuel flow unit. Escape closes.

**Cabin window.** The Fenix's flight attendant panel (FAP), in a window called "Fenix cabin". The status box at the top says whether the cabin system has started; once it has, it reads "Cabin system ready." Below it are five tabs; Ctrl+Tab moves between them. Each tab has a list of values, one per line, with its controls after the list:

- **Status**: cabin ready, cabin lights, boarding music, doors, slides, cargo doors, temperatures, water and waste, and cautions. The **Cabin ready** check box reports the cabin ready (while the FAP does not offer it yet, you hear *"Cabin ready is not available yet."*), and the **Cabin lights** list sets Bright, Dim 1, Dim 2 or Off.
- **Audio**: boarding music on or off, its channel and volume (**Volume up**, **Volume down**); the prerecorded announcements, with **Play now**, **Add to memo**, **Remove from memo**, **Clear memo**, **Play next**, **Play all** and **Stop**; and the FAP loudspeaker volume, in 5 percent steps.
- **Doors**: each door with its slide and pressure messages. Select a door or cargo door, and the button after the list opens or closes it, for example "Open door 1 left". Doors work on the ground only, as in the Fenix EFB; above 100 feet you hear *"Doors can only be opened or closed on the ground."* A line with no door control, such as an overwing slide, has no button.
- **Temperature**: the forward and aft zones, with **Forward warmer**, **Forward cooler**, **Aft warmer**, **Aft cooler** and **Reset to the cockpit temperatures**.
- **Water**: water, the preselect (**Preselect up**, **Preselect down**), waste, service panels, lavatories and cautions.

After a press, the window speaks only what your screen reader cannot: the new volume, temperature or preselect, for example *"Water preselect: 75 percent"*, with *", unchanged"* added when nothing moved; a door's line once it has opened or closed; the announcement now playing (*"Now announcing: Safety Briefing"*); and the memo (*"Memo: After Takeoff"*). A press that cannot act says why, for example *"The Fenix is not answering."* or *"The memo is already empty."* Escape closes.

**Failures window.** The Fenix's manual failures, in a window called "Fenix failures": a tree of ATA chapters, title first ("Air Conditioning, ATA 21"), with each failure under its chapter and group, named as the Fenix names it. A failure that is failed or armed says so, for example "CPC 1, failed", "CPC 1, armed: above IAS 250" or "CPC 1, armed: fails in 30 seconds".

- **Search**: type part of a name and press Enter. BA says how many failures match and moves to the first.
- **Show**: All failures, Armed, Failed, or Armed or failed.
- **Fail now** (Alt+N), **Arm** (Alt+A, or Enter on a failure) and **Clear** (Alt+C) each ask Yes or No before anything changes, with No as the default. BA then says what the Fenix reports, for example *"CPC 1 failed."* Arm asks for its condition first, in a dialog that takes Above IAS, Above altitude AMSL, Below altitude AMSL, After event (None, V1, Take-off or Landing) and a number of seconds; it needs at least one (*"Enter at least one condition."*).
- **Refresh** (F5) reads the failures again.

The status box shows your Fenix, how many failures are armed and failed, and the Fenix's automatic failure settings, which you change in the Fenix options window. While the window is open, a failure that triggers is spoken, for example *"Failure triggered: CPC 1."* Escape closes.

**Fenix documents window.** The Fenix EFB's documents: the ECAM procedures, the QRH, checklists, delay codes, the Fenix user guide and your own documents, in folders as in the EFB, in a window called "Fenix documents". The Pinned group is always first in the tree ("Pinned (0)" when nothing is pinned). Press Enter on a document to open it in the reader, a page your screen reader reads in browse mode, where H moves by heading and T by table. Colors the Fenix shows are said as words, "(amber)" and "(red)"; the flight-phase picture reads, for example, "Flight phases: 3, 4, 5"; checklist lines are check boxes; and every section starts open. In the QRH tables, PF means Pilot Flying and PM Pilot Monitoring.

- **Ctrl+F**: the search box. Type at least 3 characters and press Enter; the results appear in the tree with how many there are.
- **Ctrl+P**: open the procedure the Fenix pinned for the latest ECAM alert. Press it again for the next pinned document.
- **Ctrl+D**: pin or unpin the selected or open document. The pin shows in the cockpit EFB too. BA reads it back, *"Pinned:"* or *"Unpinned:"* and the name, which can take a few seconds because the Fenix shows the change late.
- **F6** moves between the document, the list and the search box, and **Shift+F6** goes back. **F5** reloads the list.
- **Escape** hides the window and keeps the open document for next time.

Your own Markdown documents go in the Fenix's documents folder (C:\ProgramData\Fenix\EFB\Documents, according to Fenix's guide) and appear under My Documents. PDFs are listed but cannot be read; BA tells you so. One known issue: the first document you open with Enter after opening the window may read only "document" until you press Tab once; later documents read normally.

**Fenix options window.** Every option in the Fenix EFB's Settings, with its value in the Fenix's own words, as a tree in a window called "Fenix options". A row reads "Label: value". The first group, "Options that affect MSFS Blind Assist", holds the options that can change how BA works, each with a note on what it changes. Then come the Fenix's own setting categories, in the EFB's order, and last "Other, not in the Fenix EFB". Your Hoppie, Navigraph, Pushover and SimBrief credentials read "set, hidden".

- **Enter** on any option reads its details, which the details box (Tab) also shows: for an option in the first group its note first (*"Effect on MSFS Blind Assist: …"*), then the Fenix's own description of the option, its choices, where its value comes from (the Fenix default, your livery, this simulator session or your own permanent setting), and whether the EFB lets you change it.
- **F2** changes an on/off or choice option for this simulator session, until you change aircraft or livery, as the EFB's own switch does: choose the new value in the dialog. The five options that reshape the cockpit (Standby Instruments, ISIS, ADIRS, ACP and DCDU) then ask Yes or No before anything changes.
- **Shift+F2** makes an option your permanent setting, for every flight and livery: choose the value in the dialog.
- **Ctrl+F2** resets your change to the Fenix default.
- Shift+F2 (after you choose the value) and Ctrl+F2 always ask Yes or No before anything changes, with No as the default.
- **F5** reads the options again, for example *"Refreshed, no changes."*

After a change, BA waits for the Fenix to show it, which can take a few seconds, then updates the row, and your screen reader reads it. If the row's text did not change, for example when you make the current value permanent, your screen reader has nothing new to say, so BA reads you the row itself, the option's name and value. If the Fenix did not take the change, BA says so, for example *"Not made permanent:"* followed by the option as it now is. The status box gives the Fenix version, the three keys and, after "Not what MSFS Blind Assist is tested with", any option that affects BA and is set to a value BA was not tested with. Today that is Control Position set to First Officer, because BA's audio panel controls were only checked from the Captain seat. EFIS Linking: Baro, EFIS Linking: FD/LS, Channel Spacing and FCU Readouts work with either setting, and their notes say what was heard when they were checked; Enter on an option speaks its note. Escape closes.

**First Officer: Output Shift+C.** Flows and checklists. See [The First Officer and Checklists](first-officer.md).

### Aircraft-specific hotkeys and callouts {#aircraft-specific-hotkeys-and-callouts-2}

The usual [Readout Hotkeys](core-concepts.md#readouts) and FCU readouts work, except Output D and Shift+D (distance to destination and top of descent) and the Shift+1 to Shift+6 speeds, which the Fenix does not offer. Output Ctrl+M opens the Fenix Monitor Manager.

The TO CONFIG key on the ECAM panel works like the real test. BA presses it, then watches the master warning and master caution lights. The Fenix's warning computer deliberately takes a moment to light them, so *"Takeoff config normal."* comes only once both lights have stayed dark for about two seconds. A bad configuration is announced as soon as a light comes on: *"Takeoff config: check configuration."* for the master warning, or *"Takeoff config: caution. Check the ECAM."* for the master caution. If BA could not read the lights, you hear *"Takeoff config: no result read. Check the ECAM."* The First Officer's takeoff config test says the same.

On the take-off roll, BA calls *"V1"*, *"Rotate"* and *"V2"* from the speeds on the MCDU's PERF TAKE OFF page, whether or not Takeoff Assist is on. You can enter or change the speeds any time before the roll. The Fenix itself also calls V1, so you hear V1 twice. Each call is muted with its speed's row in the Fenix Monitor Manager ("MCDU V1 Speed", "MCDU VR Speed", "MCDU V2 Speed"), which also mutes BA reading that speed when you enter it; a cleared speed is read as "not set". See [Takeoff Speed Callouts](takeoff-climb-and-cruise.md#takeoff-vspeed-callouts).

Automatic announcements cover the master warning and caution, the cockpit's annunciator lights and switch states, COM frequency changes, the altimeter, and the cockpit door (*"Cockpit Door: Open"*). The lights of Econ Flow and ACT Pump, and the lamps of the EFIS flight director and LS buttons, stay quiet right after you change their control in BA's panels, because your screen reader already says the new state. When the change comes from anywhere else, such as a click in the cockpit, the First Officer, or the other side of linked flight directors, you hear it, for example *"EFIS Right FD: On"*. The FCU vertical speed is spoken only while its window shows a selected value, with its sign, for example *"FCU VERTICAL SPEED: +300"*; a managed climb or descent stays quiet. Two things are monitored but kept silent by default: the clock's chronometer and elapsed-time counters (they tick every second) and the four seat switches. You can switch any of them back on in the Monitor Manager.

With the Fenix loaded, BA also announces these from the cabin, whether or not the Cabin window is open:

- *"Cabin ready"*, once, when the cabin crew reports the cabin ready. In Fenix v3 the virtual purser reports it; your cabin call from the cockpit no longer makes the cabin ready.
- A passenger or cargo door opening or closing, for example *"Door 1 left open"* or *"Forward cargo door closed"*. A door you open or close in the Cabin window is said only once, by the window.
- *"Slides armed"* or *"Slides disarmed"*, when the passenger doors' slides are all armed or all disarmed.
- *"Cabin caution:"* and the caution, when one appears on the flight attendant panel.

Each has its own row at the end of the Fenix Monitor Manager: "Cabin ready", "Cabin doors opening and closing", "Cabin slides armed and disarmed" and "Cabin cautions".

### Things to know {#things-to-know-2}

- **Panel buttons are pressed and released, like a real finger press.** The aircraft's light shows the result. You do not need to "un-press" anything. Bus Tie, Service Interphone Override, Avionics Compartment Light, the weather radar's GCS, ACP PA Transmit, Calls Mechanic, All, Forward, Aft, Mid and Exit, the clock's Chronometer and Date buttons, ISIS Baro Push and Pedestal Video are buttons you press once, like the real ones. One with a light shows its state on its label and the light announces the change; one without a light says, for example, *"Calls Mechanic pressed"*.
- **ADR, IR and Bus Tie read the right way round.** Their light is the OFF legend, so the button reads "ADR 1: On" while ADR 1 works and "ADR 1: Off" once you switch it off (Bus Tie reads "Auto" or "Off"), and the light announces the same words, for example *"ADR 1: Off"* or *"Electrical Bus Tie: Off"*. Their Monitor Manager rows are named "ADR 1" to "ADR 3" and "IR 1" to "IR 3".
- **ISIS Baro Push** says it was pressed, but BA cannot yet read what it changed.
- **The Fenix does not model MLS reception**, so there is no MLS switch on the audio panel.
- **The DCDU screen cannot be read** on the Fenix; only its keys are on the panel.

## FlyByWire A380X {#airbus-a380}

The FlyByWire A380X is a free simulation of the Airbus A380-842 by the same team as the A32NX. Its cockpit is a newer generation than the A320's, and BA handles it differently in important ways. BA reads the real cockpit displays live, with no add-on to install and no Developer Mode. BA also ships its own detailed A380 manual, "a380-manual.html", in its Guides folder. Some of its details are older than this guide; where they disagree, this guide describes the current version.

### Before you fly {#before-you-fly-3}

You need:

- The FlyByWire A380X, installed with the FlyByWire Installer. BA follows FlyByWire's **Development** build. FlyByWire changes things underneath from time to time, and an older copy of the aircraft may not report every readout correctly, so keep the aircraft updated.
- The MobiFlight WASM module in your Community folder.
- The displays powered (battery on, or a powered preset) so there is something to read.
- For the full airport map and Brake-To-Vacate features: a Navigraph subscription, logged in inside the flyPad.

When BA first connects to the A380 it stays silent for a moment, so you are not flooded with the whole starting cockpit state. Only changes after that are spoken.

### How the panels are organised {#how-the-panels-are-organised-3}

The A380 has five sections.

**Overhead**: **ELEC** (four batteries, four external power connections, generators, IDGs, bus tie and emergency generator), **APU**, **Fuel** (tank transfers, jettison and every feed tank pump), **Hydraulics**, **Bleed Air**, **Air Conditioning** (cockpit and cabin target temperatures), **Pressurization**, **Ventilation**, **Cargo Air**, **Anti Ice** (probe and window heat, wing, four engines), **Fire**, **Oxygen**, **Calls** (including evacuation and "Signal Cabin Ready"), **Signs**, **Wipers**, **ADIRS**, **Flight Control Computers** (PRIMs, SECs, rudder trim and the nosewheel steering pedal disconnect), **Engine FADEC and Manual Start**, **Recorder and Misc**, **GPWS**, **Reset** (the computer reset buttons, such as Reset FMC A), **Interior Lighting** and **Exterior Lighting**.

**Glareshield**: **FCU**, **EFIS Captain**, **EFIS First Officer**, **Warnings** and **OIT**.

**Instrument**: **Gear** (including gravity extension), **Autobrake** (Disarm, BTV, Low, L2, L3, High, and RTO arm), **ISIS**, **Source Switching** and **Clock** (chronometer start and stop, reset, and the elapsed time knob).

**Pedestal**: **Engines** (engine mode selector and four masters), **Thrust Levers**, **Flaps and Brakes** (flaps, parking brake, anti-skid), **Speed Brake**, **ECAM Control Panel**, **Weather Radar**, **Transponder**, **RMP**, **Audio Control Panel Captain**, **Audio Control Panel First Officer**, and **Cockpit** (sliding windows, sunshades, tables, footrests, keyboard trays, the cockpit door and the seats).

**Displays**: **PFD**, **ND**, **Status**, **Speeds** and **Minimums**, which are read-only status displays.

Some A380 controls are worth knowing about:

- **EFIS**: each side has LS, V/V, constraints, airport and traffic switches, ND mode and range, navaid 1 and 2, the weather or terrain overlay, the airport map range, and one **ND Filter** control offering Off, Waypoints, VOR/DME and NDB. These are alternatives on the A380: choosing one replaces the last. Once one is chosen, the aircraft has no way to go back to showing none, so picking "Off" tells you so rather than appearing to work.
- **Flight directors**: on the A380 both flight directors are one pushbutton, as on the real aircraft. One press switches both.
- **Interior Lighting** has the main panel flood and side console dimmers as sliders, the storm light, lighting presets, and cabin brightness from 0 to 100 percent.
- **RMP** in the Pedestal is a quick read-only glance at the three radio panels. To tune radios, use the RMP window described below.
- **Audio Control Panel** lets you check and switch which VHF radios you are listening to and transmitting on, and set their volume.
- **Aircraft presets** (Cold and Dark, Powered, Pushback, Taxi, Takeoff) are loaded from the flyPad's Presets page. BA announces the progress, ending with *"Aircraft preset loading complete"*.

### Autopilot / FCU windows {#autopilot-fcu-windows-3}

The A380 has the same set of FCU windows as the A32NX, in Input mode: Ctrl+S speed, Ctrl+H heading (with HDG·V/S and TRK·FPA), Ctrl+A altitude, Ctrl+V vertical speed or flight path angle, Ctrl+P the autopilot panel and Ctrl+B the altimeter ("Set A380 Altimeter (both sides)"). Two differences:

- The Altitude window has a **Metric (MTRS)** button, which reads "Metric (MTRS) — now OFF (press for metres)" or "now ON (press for feet)". With metric altitude on, the value you type is in metres (30 to 14,935).
- The Autopilot panel has an **FD** button for both flight directors, and no EXPED button, because the A380 FCU has none.

Pushing or pulling the altitude knob announces *"Altitude Mode: Managed"* or *"Selected"*, and Output Shift+A tells you which one you are in.

The shortcut and push and pull keys are the same as on the A32NX.

### MCDU {#mcdu-3}

The A380 does not have a classic MCDU. It has two large MFDs (multi-function displays), operated with a keyboard and trackball called the KCCU. BA hides all of that: Input Shift+M opens the MFD as a flat list you arrow through, and BA drives the real display underneath. You never need the cursor or trackball.

The window title says which side you are using, for example "A380X MCDU (Captain)". The **Active MCDU side** selector switches between Captain and First Officer, which are independent on the real aircraft.

Each line ends with a word that tells you what you can do with it:

- **button**: press Enter to activate it. Flight plan waypoints and discontinuities are buttons too; Enter opens their menu.
- **edit**: a field you can type into. An empty one reads "blank".
- **combo box**: a dropdown, which also says whether it is collapsed or expanded and what is selected. Press Enter to open it; its choices then appear right underneath as "option" lines.
- **option**: a choice in an open menu. Arrow to it and press Enter.
- **dimmed**: the aircraft is not accepting that control right now. This is usually correct aircraft behaviour, not a fault.

To enter a value:

1. Arrow to the field.
2. Type the value. It goes into the scratchpad (Alt+S also jumps there).
3. Press Enter. BA sends the scratchpad to the selected field.

To clear: Backspace (or Alt+C) works like the Airbus CLR key. It clears your scratchpad first, then a displayed message (*"Info cleared"*), then the selected field. Delete always clears the selected field. A field that cannot be emptied says *"Cannot be cleared"*. A value typed into a pick list that does not offer it is reported as *"Not in list"*.

To move between pages there are three ways:

- The **Go to MFD page** combo at the top reaches every page: the ACTIVE pages (F-PLN, PERF, FUEL &amp; LOAD, WIND, INIT), POSITION, SEC INDEX and the secondary flight plans, DATA, ATC COM and SURV. Arrow to a page and it opens once your selection settles.
- The Alt page buttons: Alt+I INIT, Alt+F F-PLN, Alt+E PERF, Alt+L FUEL &amp; LOAD, Alt+R RAD NAV, Alt+P SEC FPL, Alt+D DIR, Alt+A ATC COM, Alt+V SURV.
- Ctrl+Up and Ctrl+Down (or PageUp and PageDown) step to the previous or next page and, on the flight plan, scroll it. You hear *"Previous page"* or *"Next page"*.

F5 refreshes. Alt+N switches BA's weight readouts between kilograms and pounds.

Reading the flight plan: on F-PLN each waypoint is one line that includes the leg leading to it and any constraint, for example "BI613, via BASU1D, 5 NM, track 294°, at or below 5000 feet". The page shows about a dozen waypoints at a time with the destination at the bottom, so press Ctrl+Down to see the next part of the route.

Changing the flight plan: press Enter on a waypoint to open its revision menu (direct to, insert next waypoint, delete, hold, airways, overfly, departure, arrival). Some changes create a temporary plan; scroll to the bottom and press Enter on the insert option to keep it, or the erase option to cancel it. To clear a discontinuity, press Enter on the "DISCONTINUITY" line and choose DELETE.

Loading a SimBrief route: set your SimBrief ID in the flyPad first and do not set a destination on the MSFS world map. Then on INIT (Alt+I) press Enter on "CPNY F-PLN REQUEST", wait for the scratchpad to say the company flight plan has been received, press Enter on the received flight plan, and choose INSERT.

The MFD WIND page works too. Every wind field says its phase, row and column, so you can enter winds and request, insert or clear a company wind uplink.

ATC COM (Alt+A) gives you the datalink pages: connection, message record (open it when BA announces an ATC message waiting) and D-ATIS. Some ATC COM pages are still unfinished on FlyByWire's side and show an error page.

### EFB {#efb-3}

The flyPad opens with Input Shift+T in a window called "A380X flyPad EFB". It works exactly as on the A32NX: a browsable web page with headings, links and real edit fields. See the A32NX section above and [Aircraft EFBs](planning-your-flight.md#aircraft-efb).

On the A380, the door tiles use the aircraft's real door names, such as "Main Door 1 Left" and "Upper Door 1 Left". The flyPad's own checklist page is only a tracker on the tablet; ticking things there does not touch the aircraft. For the real cockpit checklist, use the live ECL below. BA does not make the flyPad's pushback page accessible; use GSX for pushback (Input Alt+G opens the Access GSX window).

The flyPad is also where you log in to Navigraph, which the airport map needs.

### Special windows {#special-windows-3}

**Radio Management Panel: Input Ctrl+Shift+R.** This window reads the live RMP screen as text, and you operate it entirely with keys. The **Side** selector chooses Captain, First Officer or Overhead; the screen, announcements and keys all follow it. The status line shows, for example, "Live — Captain — VHF page".

To tune a VHF radio:

1. Press Alt+V for the VHF page.
2. Press Ctrl+1, Ctrl+2 or Ctrl+3 to select radio 1, 2 or 3 (or F1 to F3 with alternate keys). You hear *"Radio 1"*.
3. Type the frequency without a decimal point. It completes itself: 118500 or just 1185 both give 118.500. Backspace deletes a digit.
4. Press Enter. The frequency goes into standby and BA reads it back, for example *"VHF standby 1, 118.500"*.
5. Press Alt+1, Alt+2 or Alt+3 to swap that radio's standby and active frequencies. You hear *"Swapped"*.

Alt+C fully clears a stuck entry.

To set the squawk:

1. Press Alt+T for the transponder page.
2. Type the four digits (0 to 7). It sets on the fourth digit and reads back, for example *"Squawk 2222"*.
3. Alt+I sends IDENT.

Alt+Home returns to the screen from a button. The HF, TEL, NAV and MENU keys do nothing, because FlyByWire does not model them yet. Any message the RMP shows, such as an invalid frequency, is announced.

Note that Ctrl+Shift+R in Output mode reads your last landing rate instead.

**Live Electronic Checklist (ECL): Output Ctrl+Shift+C.** The A380's real checklists, as the cockpit uses them, including any abnormal procedure an ECAM alert raises. The checklist appears on the aircraft's display when the window opens and is hidden again when it closes.

- Up and Down arrow move the real checklist cursor and read each line, including whether it is checked.
- Enter opens the highlighted checklist from the menu, or ticks the highlighted item.
- Backspace goes back to the menu.
- The **Abnormal procedures** button lists active ECAM procedures.
- F5 refreshes and Escape closes.

Sensed items tick themselves when the aircraft detects the action. Set the parking brake, for example, and you hear the item followed by *"checked"*. If the window shows "Checklist not reachable", make sure the aircraft is loaded and its displays are powered, then press Refresh.

Output Shift+C opens the A380's First Officer, with its flows and its own checklists. See [The First Officer and Checklists](first-officer.md).

**Airport Map and BTV (OANS): Output Ctrl+Shift+B.** On the real A380 you arm Brake-To-Vacate (BTV) by tapping the runway and exit on a moving airport map. This window lets you pick them from lists instead and reads the predicted stopping distances. It never changes or zooms your navigation display. It has three tabs:

- **Map and BTV**: choose the **BTV runway** and press **Arm runway**, then choose the **BTV exit** and press **Arm exit**. **Clear BTV** removes the selection. The exit list only offers exits valid for the armed runway, closest first, each with its distance down the runway. The status box below reads back the runway and landing distance available, the exit and its distance, the predicted dry, wet and live stopping distances (worked out on approach, not on the ground), runway occupancy and turnaround times, and any runway-ahead caution. If the aircraft rejects an exit, BA tells you.
- **Airport**: type an ICAO code in **Airport code** and press **Display airport**, or use the **Origin**, **Destination** and **Alternate** buttons. You hear *"Loading airport"* and the code.
- **Status**: whether the airport database is loaded, and the AIRAC cycle.

The top line tells you what is available, for example "OANS: full" followed by the airport, or *"OANS: Navigraph maps NOT loaded — manual BTV only. Log in via the flyPad EFB (Shift+T) → Navigraph."* Without Navigraph maps, a manual BTV section lets you type a stop distance in metres and press **Apply**, then select the landing runway in the FMS. The maps only fill in once the ADIRS are aligned and an airport is loaded.

Arming BTV takes two steps, and both are needed:

1. Arm a runway and an exit in this window, choosing an exit beyond the predicted wet stopping distance.
2. Set the **Autobrake** combo (Instrument section, Autobrake panel) to BTV.

Only then does the aircraft arm BTV, and BA announces *"Brake To Vacate: Armed"*. If you never hear that, check the autobrake. For taxi directions after landing, use BA's own [Landing Exit Planner](approach-and-landing.md#landing-exit) and [Taxi Guidance](taxi-guidance.md); they do not need Navigraph.

**E/WD window: Output Alt+E.** Engine readings, memos and the active warnings with their action lines, read with the arrow keys. F5 refreshes, Escape closes.

**PFD, ND, ISIS and System Display.** Read the PFD (flight mode annunciator, approach capability, PFD messages), ND, Speeds and Minimums panels in the Displays section, and the ISIS panel in the Instrument section. For the System Display, use the **System Display Page** combo on the ECAM Control Panel (Pedestal). It lists the A380's 16 system pages. Choosing one switches the real system display and reads that page's values: per-tank fuel, temperatures, pressures, door states, engine readings and so on.

### Aircraft-specific hotkeys and callouts {#aircraft-specific-hotkeys-and-callouts-3}

The Output readouts are the same as the A32NX's (Shift+1 to Shift+6 for the characteristic speeds, Ctrl+W, D, Shift+D, B and so on), with these differences:

- Ctrl+0 reads the approach capability as LAND 2, LAND 3 single or LAND 3 dual.
- With metric altitude (MTRS) on, Q, A, Shift+A and the altitude panel fields all speak metres, and the altitude callouts come every 300 metres and say the unit, for example *"3000 meters"*. The standby ISIS always stays in feet.
- Ctrl+E switches the E/WD callouts on and off, and the choice is remembered.

The A380 announces everything the A32NX does, and also:

- Thrust lever detents as the levers reach them, for example *"All thrust levers"* followed by the detent.
- The full warning stream from the E/WD: failure titles and their action lines in the order a crew reads them, memos, and the small status reminders at the bottom of the display (status, advisory, failure pending).
- Overhead pushbutton fault lights.
- Approach capability changes.
- *"Altitude constraint armed"* or *"Cruise altitude armed"*. On the A380 the PFD marks a constraint only by a colour change, so this callout is the only way to hear it.
- Runway overrun protection: *"Runway too short"* and *"If wet, runway too short"* on approach, *"Max braking"*, *"Set max reverse"* and *"Keep max reverse"* on the rollout, and *"Runway ahead"* when taxiing towards a runway.
- During a BTV rollout, the BTV state and a countdown such as *"500 meters to exit"* and *"300 meters runway remaining"*.
- *"V1"*, *"Rotate"* and *"V2"* from the speeds entered on the PERF page.
- Speed protection, minimums set or cleared, and weight unit changes.

All of these can be muted in the A380 Monitor Manager (Output Ctrl+M); for example "BTV Distance to Exit", "Runway Remaining" and "Brake To Vacate" each have their own row.

### Things to know {#things-to-know-3}

- **The A380 is a development build.** FlyByWire marks some features as unfinished, for example parts of ATC COM, FLS approaches and the second ISIS. If something reads as an error page or does nothing, it is often the aircraft, not BA.
- **Wing anti-ice works, but the "Wing Anti-Ice Flowing" readout may still say there is no flow.** FlyByWire has not yet modelled the A380's wing anti-ice air supply.
- **Surroundings window frequencies:** on the A380, pressing Enter on a frequency in the Surroundings window does not tune it. It reminds you to tune COM 1 with the RMP window instead.
- **The A380 always keeps a destination runway.** To change the arrival, choose a different runway or approach; you cannot remove it entirely.
- **TCAS resolution advisories are inhibited on the ground and close to it.** Hearing only traffic advisories while taxiing is correct.

## Headwind A330-900neo {#airbus-a330}

The Headwind A330-900neo is a free widebody built on FlyByWire's A32NX code. BA treats it as an A32NX with a few differences, so almost everything in the A32NX section above applies here unchanged: the same panels and sections, the same FCU windows, the same MCDU, the same flyPad, the DCDU and the E/WD window. Its hotkey list is the A32NX list.

### Before you fly {#before-you-fly-4}

You need the Headwind A330-900neo, the MobiFlight WASM module, and the displays powered. SimBridge is optional, as on the A32NX. Headwind's A330 is an early release, so some of BA's features on it have been checked less thoroughly than on the A32NX. Report anything that reads wrong.

### How the panels are organised {#how-the-panels-are-organised-4}

Exactly as on the A32NX: Overhead, Glareshield, Instrument and Pedestal, with the same panels. See [the A32NX panels](#airbus-a32nx). The PFD status box leaves out a few rows the A320 shows (the stall warning and alpha protection speeds and VAPP), because the A330 does not publish them.

### Autopilot / FCU windows {#autopilot-fcu-windows-4}

The same windows and keys as the A32NX.

### MCDU {#mcdu-4}

Input Shift+M opens the MCDU, with the same keys as the A32NX MCDU. It reads the A330's own MCDU display.

### EFB {#efb-4}

Input Shift+T opens the flyPad in a window called "A330 flyPad EFB". It is the same flyPad as on the A32NX.

### Special windows {#special-windows-4}

- **DCDU: Input Ctrl+Shift+D**, exactly as on the A32NX, including the two-step answer and the need for a datalink connection.
- **E/WD: Output Alt+E.**
- **PFD, ND, ISIS and System Display** from the Instrument section panels.
- **First Officer: Output Shift+C**, with the A330's flows and checklists.

### Aircraft-specific hotkeys and callouts {#aircraft-specific-hotkeys-and-callouts-4}

The hotkeys and automatic callouts are the A32NX's, including *"V1"*, *"Rotate"* and *"V2"*. The differences are in how a few readouts get their data:

- **Altimeter (Output B):** BA reads the simulator's own altimeter on the A330, because the FlyByWire baro values do not reach BA on this aircraft. You hear for example *"Altimeter: 1013, 29.91"*, with the unit the FCU is set to read first, or *"Altimeter standard"*. Knob changes in the cockpit are announced the same way. If nothing has been read yet you hear *"Altimeter not available"*.
- **Characteristic speeds (Shift+1 to Shift+6):** the A330 still publishes its speeds the older way, and BA reads them from there, so VFE and VS keep working on this aircraft.
- **Distance to destination (Output D):** the A330 keeps this in an older place, and BA looks there too.

### Things to know {#things-to-know-4}

- **The Ctrl+B altimeter window** sets both sides as on the A32NX. Its own change is not announced twice.
- **Visual guidance and taxi guidance use widebody settings** on the A330: a heavier, faster approach profile and a slightly earlier turn cue when taxiing, to suit the longer aircraft.

## iniBuilds A350 {#airbus-a350}

The iniBuilds A350 is a payware simulation of the Airbus A350-900, A350-900 ULR and A350-1000, which share one cockpit. Like the A380 it is a newer generation than the A320: two MFDs worked from a KCCU keyboard instead of MCDUs, and an OIS (onboard information system) on each side instead of a tablet. Its displays are pictures drawn by the aircraft's own code, so BA reads every one of them by running that same code on a private copy of the aircraft's memory. Nothing is written into the simulator, and there is no add-on to install and no Developer Mode. BA was built and tested on A350 version 1.2.7.

### Before you fly {#before-you-fly-5}

You need:

- The iniBuilds A350.
- The MobiFlight WASM module in your Community folder. Every switch, knob and key BA operates on the A350 goes through it, and BA warns you a short while after connecting if it is missing.
- The displays powered (batteries on, with external or APU power, or a powered panel state from the OIS) so there is something to read.

The first time you fly after installing or updating the A350, BA prepares its display reader. You hear *"A350 displays getting ready"*, and about 15 seconds later *"A350 displays ready"*. Until then a display reads *"The A350 displays are not available yet."* The next start takes a moment only.

When BA first connects, it stays silent while the aircraft's values settle, so you are not flooded with the whole starting cockpit state. Only changes after that are spoken.

### How the panels are organised {#how-the-panels-are-organised-5}

The A350 has six sections. Its panels take the A380's names and order wherever the two cockpits share a panel, so if you know the A380 you will find your way. Where each pilot has controls of their own, the shared part comes first, then a Captain panel and a First Officer panel right after it, for example **Captain Wiper** and **First Officer Wiper**.

**Overhead**: **ELEC**, **APU**, **Fuel**, **Hydraulics**, **Bleed Air**, **Air Conditioning**, **Pressurization**, **Ventilation**, **Cargo Air**, **Anti Ice**, **Fire**, **Cargo Smoke**, **Cabin Smoke**, **Oxygen**, **Calls**, **Evacuation**, **Signs**, the two wipers, **ADIRS**, **Flight Control Computers**, **Engine FADEC and Manual Start**, **Recorder and Misc**, **Surveillance**, **Cabin**, **OIS**, **Maintenance**, **Interior Lighting** and **Exterior Lighting**.

**Glareshield**: **FCU**, **EFIS Captain**, **EFIS First Officer**, each pilot's **Warnings** (chrono, master caution and warning, autoland and ATC message), each pilot's **HUD**, and each pilot's **Loudspeaker and Table Light**.

**Instrument**: **Gear**, **Autobrake**, **ISIS**, **SND** (the standby navigation display), **Source Switching**, **Display Power** with its Captain and First Officer parts, and each pilot's **Side Panel** and **Individual Panel**.

**Pedestal**: **Engines**, **Thrust Levers**, **Captain RMP**, **First Officer RMP**, **Emergency Descent**, **Flaps and Brakes**, **Speed Brake**, **Trim**, **ECAM Control Panel** with its Captain and First Officer parts, **Weather Radar** with its Captain and First Officer parts, **Transponder**, **Cockpit**, each pilot's **Display Transfer**, and **RMP 3**.

**Cockpit**: what sits around you rather than on a panel: **Doors and Hatches**, the sun shades, the oxygen masks, the HUD units, each pilot's tiller and sidestick (with its takeover button), and **Misc**.

**Displays**: each display read as text with its colours, refreshed every second: the Captain and First Officer PFD, ND and MFD, the FCU display, the three RMP displays, the standby flight display, the standby navigation display and the BAT display.

Some A350 controls are worth knowing about:

- **Buttons show their state** beside their name wherever the cockpit shows one: their light (On or Off), their lit legends ("EXT 1: Available"), or what they switch ("SPD/MACH: Knots", "Captain baro unit: Hectopascals"). Each EFIS VOR 1 and VOR 2 key reads VOR, ADF or Off: each press steps to the next (ADF only when the ADF option is fitted), and its light is on for VOR and ADF alike.
- **The baro knobs**: on the A350 a push selects STD and a pull QNH, the other way round from the A320.
- **Typed values**: a knob whose value the cockpit shows is a field you type into, as on the other Airbus aircraft: the FCU speed, heading, altitude and V/S, each side's QNH (in that side's unit), the ISIS QNH, and the cabin altitude and vertical speed targets. Type the value and press Enter. The aircraft's own knob lands it, and BA says what the cockpit then shows, for example *"FCU speed 250, selected"*. A value the knob cannot take is refused with the reason, for example a Mach number while the window shows knots, or a QNH while that side is on STD.
- **Levers**: the thrust levers, flaps and speed brake are combos with the A380's positions. The rudder and pitch trims are nudge buttons, with the trim shown on their panel ("Pitch trim: 1.3 degrees up").
- **Runway condition** (Autobrake panel) is one choice from 6 dry to 1 ice. BA opens the knob's menu on the system display, steps to your choice and closes it.
- **Options**: the HUD, the standby navigation display, fuel jettison and a few other parts are options, set on the OIS options page. Without one, its controls say so, for example *"HUD is not fitted"*.
- **System display page** on the ECAM Control Panel shows the SD page and picks one.
- A switch the aircraft did not move is said once, for example *"BAT 1 did not move"*.

### Autopilot / FCU windows {#autopilot-fcu-windows-5}

The A350 has the A380's FCU windows, in Input mode: Ctrl+S speed (knots, or a Mach number such as 0.85, with SPD/MACH), Ctrl+H heading (with HDG-V/S / TRK-FPA), Ctrl+A altitude (100 to 49000 feet, with 100/1000, metric altitude and ALT level off), Ctrl+V vertical speed or flight path angle, Ctrl+P the autopilot panel (AP 1, AP 2, LOC, APPR, FD, A/THR, ALT and the A/THR disconnect, each labelled with its light, for example "AP 1: On") and Ctrl+B the altimeter. Each value window has the knob's push and pull, and keeps the window's line current in a read-only box.

- A value typed into a managed or dashed window is a preset that the pull then uses, as on the real aircraft. BA tells you so, for example *"FCU speed 260, preset"*.
- The altimeter window ("A350 Altimeter"): a QNH typed once sets both sides and the ISIS, each in that side's unit; STD or QNH on both sides; and each side's baro unit button, labelled with the unit it shows.

The autopilot and knob keys are the A380's: Input Shift+A autopilot 1, Ctrl+O autopilot 2, Shift+P approach, Ctrl+J autothrust and Ctrl+L localizer; Shift+1 to Shift+4 push and Ctrl+1 to Ctrl+4 pull the heading, altitude, speed and vertical speed knobs. Each one presses the FCU's own control, and the flight mode announcements tell you what changed.

### MCDU {#mcdu-5}

Like the A380, the A350 has MFDs instead of MCDUs, worked from the KCCU. Input Shift+M opens the MFD window ("A350 MFD"): the page as a list of lines, each with its value and colour. BA drives the real display underneath, so you never need the cursor or trackball.

- Up and Down move through the lines. Enter acts on the line: a field takes what you typed in the scratchpad box (Tab to it, type, then Enter); anything else is clicked.
- Backspace presses CLR, which clears what the MFD is typing. Page Up and Page Down scroll. F5 refreshes and Escape closes.
- To delete a value, type CLR in the scratchpad box and press Enter on its field, as on an MCDU. For a navaid you tuned by hand on RAD NAV, do it on its ident line: the FMS tunes it again itself.
- The **Side** box picks the captain's or the first officer's MFD.
- Alt and a letter presses a KCCU page key: Alt+I INIT, Alt+F F-PLN, Alt+E PERF, Alt+L FUEL, Alt+R RAD NAV, Alt+P SEC, Alt+D DIR, Alt+A ATC, Alt+V SURV, Alt+C CLR, Alt+T DEST, Alt+G PROG, Alt+M MAILBOX, Alt+K C/L MENU, Alt+N NOTEPAD, Alt+O DATA, Alt+U MENU, Alt+B AIRPORT, Alt+Y REF and Alt+X ESC.
- A new page says its title, and a menu that opens takes you to its first item.

Reading the flight plan: each F-PLN waypoint is one line with its leg, labelled by the page's columns. A constraint says whether it is met, for example "ALT 5000 (constraint met)". Press Enter on a waypoint to open its revision menu.

The MFD's own symbols are said in words. An arrow between two words of a key is the mode it switches to, for example "SPD to MACH". A triangle before a word is a delta, as in "delta FUEL".

FCU BKUP, the last item of the MFD's application menu (the first line of the page), is the backup of the FCU and both EFIS control panels. Its keys stay grey and do nothing while the FCU works, as in the aircraft. Keys that share a name say their column, for example "SPD SELECTED button" and "MAG HDG MANAGED button".

Now and then BA catches a label while the aircraft is redrawing it. It then says "unreadable" instead of the label. The window keeps showing that line as it last read it, and the next refresh, or F5, brings the label back.

FMS messages, such as *"Scratchpad: NAV ACCURACY UPGRADED, white"*, are spoken as they appear: by the window while it is open, and in the background while it is closed. The background ones have their own Monitor Manager row, "FMS messages (MFD scratchpad)".

The A350's normal checklists are on the MFD's C/L MENU (Alt+K in the window, or Output Ctrl+Shift+C). Each item is a button: Enter ticks it, and sensed items tick themselves.

Three keys you know from other aircraft open the MFD window straight on the A350's own page: Input Ctrl+N on RAD NAV (the navigation radios), Output Ctrl+Shift+C on C/L MENU (the checklists) and Input Ctrl+Shift+D on ATC COM (the datalink). If the MFD is still typing your last entry, BA says so and stays on the page: press the key again.

### EFB {#efb-5}

The A350's EFB is its OIS, one on each side. It opens in the window the PMDG tablets use: Input Shift+T for the captain's ("A350 OIS") and Input Ctrl+Shift+T for the first officer's ("A350 OIS (First Officer)"). Every page reads in browse mode: FLT OPS, T.O PERF and LDG PERF (type your values, pick the runway, press COMPUTE, and the results are spoken once they arrive, for example *"Results: V1: 141 kt, ..."*), the loadsheet and refuel, charts (the charts themselves are pictures), company messages, GROUND (each door with its state, lights, chocks, pushback and GSX), MAINTENANCE, the panel states, autosave, the options and, on the side chosen as the preferred seat side, the Situation Manager with its failures, each Active or Inactive. The OIS's own messages, errors and confirmation questions are spoken as they appear.

If you turn on STREAMER MODE in the OIS options, your SimBrief ID, API keys and logon codes are hidden in BA too: you hear only how many characters they have, for example "SIMBRIEF USER ID, hidden, 7 characters", and you can still type into them.

Output Ctrl+Shift+T stays the Cold Temperature Altitude Correction, as on the PMDG aircraft.

### Special windows {#special-windows-5}

**Radio Management Panel: Input Ctrl+Shift+R.** The chosen RMP's screen as a list ("A350 RMP"): each radio's active and standby frequency and the squawk, kept up to date. The **RMP** box picks the Captain's RMP, the First Officer's or RMP 3.

To tune a VHF radio:

1. Press Ctrl+1, Ctrl+2 or Ctrl+3 to pick the radio line.
2. Type the frequency. The digits and the point go to the cockpit RMP's keypad as you type; the leading 1 and the point are optional, so 2 1 9 is 121.900 and 8 7 is 118.700. The RMP never shows what you type, so Backspace deletes the last digit and tells you what is left.
3. Press Enter. The frequency becomes that radio's standby, and BA reads the line back (*"standby unchanged"* if the RMP refused it).
4. Press Alt+1, Alt+2 or Alt+3 to swap that radio's standby in. BA reads it back.

A frequency typed and not loaded is deleted when you press another key, for example *"272 cleared"*. Alt+T, then four digits from 0 to 7, sets the squawk on the fourth digit; Escape cancels. Alt+V, Alt+H, Alt+L, Alt+N and Alt+U press the VHF, HF, TEL, NAV and MENU page keys; the A350 draws only the VHF page, so the others say *"screen unchanged"*. F5 refreshes and Escape closes. Every RMP key, microphone key, volume knob and reception is also on the Pedestal's RMP panels.

Note that Ctrl+Shift+R in Output mode reads your last landing rate instead.

**E/WD window: Output Alt+E.** The engine block, every warning display line with its colour, the ECAM cursor ("selected") and each action box ("to do", "done"), the SD page and its permanent data line. Read with the arrow keys; it updates itself, F5 refreshes now and Escape closes.

**Displays.** Output Alt+P reads the captain's PFD (the flight mode annunciator first, then speeds, altitude, heading and messages), Alt+N the captain's ND, Alt+S the SD page shown with its values named, and Alt+I the standby flight display. Every other display is in the Displays section.

### Aircraft-specific hotkeys and callouts {#aircraft-specific-hotkeys-and-callouts-5}

The Output readouts are the A380's, from what the A350 itself shows:

- Shift+H, Shift+S, Shift+A and Shift+V read each FCU window as the FCU shows it, for example *"FCU speed 250 knots, managed"*.
- Shift+4 reads VLS and Shift+6 VFE next. Shift+5 reads the alpha protection and alpha max speeds: the A350's PFD shows no stall speed, and its low-speed band starts there. Shift+1, Shift+2 and Shift+3 read green dot, S and F speed off the captain's speed tape (*"below the speed scale"* while beyond its view, *"not shown"* while not drawn).
- D reads the distance to destination along the route and the time at your ground speed; Shift+D the distance to the top of descent.
- Ctrl+W reads the ND's TO waypoint, and Ctrl+0 the approach capability as the flight mode annunciator shows it.
- F and Shift+F read the fuel on board, W and Shift+W the gross weight and centre of gravity, from the system display.
- X reads the squawk and transponder mode, as the captain's RMP shows them.
- With the FCU's METRIC ALT button on, Q and A read metres and the altitude callouts come every 300 metres, for example *"3000 meters"*.
- Ctrl+E turns the E/WD callouts off and on.

What the A350 says on its own, each with its own row in the A350 Monitor Manager (Output Ctrl+M):

- Switches and selectors that something other than you moves, for example *"Seat belts: On"*, and lights as the cockpit lights them (*"BAT 1 fault light on"*, *"Master caution on"*). A light that only blinks says nothing.
- *"pressed"* after a button's name, for a button with no light or state of its own. A knob you step says the value the cockpit then shows.
- The FCU altitude window and each side's baro when they change other than by your own entry: *"Altitude 12000 feet"*, *"Captain altimeter 1013 hectopascals"*, *"First officer altimeter standard"*.
- *"IRS aligned"*, once, as the last IR that is on finishes aligning.
- The take-off speeds as the FMS takes them (*"V1: 142 knots"*), and *"V1"*, *"Rotate"* and *"V2"* on the take-off roll.
- The approach minimums as PERF APPR takes them: *"Baro minimum 520 feet"*, *"Decision height 200 feet"*, *"No DH"*, and *"Baro minimum cleared"* when one goes, for example when you enter the other (the A350 keeps one) or change the approach. The FMS's own reset after landing says nothing.
- Where the thrust levers, flaps and speed brake went (*"All thrust levers Climb"*, *"Spoilers 40 percent"*).
- The OIS's red cover banners, with its window closed: *"Engine covers are still in place."* and, on the ground, *"Pitot covers are still in place."*, as they appear and once after a flight loads with them showing. Take the covers off on the OIS GROUND page (COVERS).
- VHF 1 and VHF 2 frequencies and the squawk when they change, wherever they were changed: *"VHF 1 standby 121.900"*, *"VHF 1 active 121.900"*, *"Squawk 4321"*. A squawk you set in the RMP window is said once, by the window.
- Each door as it opens and closes, whatever moved it (the OIS, GSX): *"Door L1 open"*, *"Forward cargo door closed"*, and *"Fuel truck connected"*. The **Doors and Hatches** panel shows every door's state.
- Every E/WD line as it appears, with its colour, from power-up on.
- Each flight mode annunciator change with its colours, for example *"FMA vertical: SRS, green"*.
- The flags and warnings the PFD raises, with their colours, for example *"PULL UP, red"*, *"ATT, red"*, *"TCAS, amber"*, *"AUTO BRK OFF, amber"*, *"DECEL, green"*. Said once as each appears; the PFD's own status display shows what is up now.
- FMS messages while the MFD window is closed.
- *"TCAS: Climb. Fly vertical speed plus 1500 feet per minute."*: the TCAS's advisories in its own words, with the vertical speed its guidance commands, as they sound; *"Traffic"* and *"Clear of conflict"* on their own.
- *"Printer: FM ACTIVE TAKE-OFF DATA"* with the printout's title, a moment after the printer runs.
- *"Memory low: 9 gigabytes left. The sim may close soon: save the flight in the OIS, then restart it."* when your PC is running out of memory. Long A350 flights make the simulator grow until it closes without warning, so this is said once below 10 gigabytes and once more below 5.
- *"A350 module stopped responding: its displays and controls no longer work"* if the aircraft's own software stops (its displays freeze and nothing you press works), and *"A350 module responding again"* if it recovers. A paused simulator never counts.

### The ND and its airport map (ANF) {#a350-nd-window}

**Output Ctrl+Shift+B** opens the ND window: the A350's airport map (the ANF, its version of the A380's OANS) and Brake To Vacate live on the ND, and this window reads the ND line by line and does what a click does there. The ANF needs your Navigraph account linked on the OIS (FLT OPS MENU, OPTIONS, 3RD PARTY); without it the ND says *"ARPT NAV NOT AVAIL (NAVIGRAPH)"*, and Brake To Vacate still works by distance on the LANDING tab.

- **Side**, **ND mode** and **ND range** at the top are that side's own EFIS knobs. The range's Zoom positions show the airport map. On Arc and the Rose modes the map follows the aircraft; **Plan** lets you look anywhere, and at other airports.
- **The list** holds what the ND shows, then every name on the map with where it lies, nearest first: *"Stand A7: 2 o'clock, 75 m"*, *"Taxiway M6: north west, 75 m from the map's centre"* on Plan, *"Flag: 3 o'clock, 205 m"*. A name marked *"off the screen"* is known but needs the map moved first. On the ordinary ranges the list holds the waypoints the ND shows instead, with their colour, clock position and distance (*"TVM, green: 3 o'clock, 190 NM"*), and the **Aircraft symbol button**, which opens the same direct-to menu a sighted pilot's click on it does.
- **Enter** does on a line what a click does: a name on the map opens the ND's menu for that spot, whose items then come first in the list (SET LDG RWY, SET BTV EXIT or UNSET BTV EXIT, DECLUTTER RWY, ADD CROSS, ADD FLAG, MAP DATA, ERASE ALL CROSSES, ERASE ALL FLAGS, CENTER ON ACFT; DELETE CROSS or DELETE FLAG on a mark). A greyed item says *"grey"*: it does nothing now.
- **Functions button** opens the panel with its four tabs. **MAP DATA:** choose RWY, TWY, STAND or OTHER, then pick an item from the **Map item list button**, or type in the field; it shows the runway's lengths and offers ADD CROSS, ADD FLAG and CENTER MAP ON (Plan only). **LANDING:** MODIFY RWY, LDG PERF CODE, and the distance for BTV. **ARPT SEL:** type an ICAO or IATA code or a city name, or press ORIGIN, DEST or ALTN, then DISPLAY AIRPORT (Plan only); the airport's name reads yellow while it is only selected and green once shown. **STATUS:** the active and second airport databases and SWAP.
- **The text box at the bottom** is for a field: type, then press Enter on the field. **Control plus an arrow** drags the map that way (on Arc and Rose it springs back when let go, as it does for a sighted pilot). **Page Up and Page Down** are the KCCU's arrows, which move through an open list longer than its 4 rows. **Backspace** is CLR, **Alt+X** the KCCU's ESC, **F5** reads now, **Escape** closes.

**Brake To Vacate:** select the landing runway in the flight plan, set the runway condition (Autobrake panel), set the ND to Plan and a Zoom range, press the taxiway's name on the runway and choose SET BTV EXIT, or enter a distance on the LANDING tab, then press A/BRK. The ND then shows the runway and its landing distance, the condition, the exit, the occupancy and turnaround times, and the FMA says BTV. An exit before the wet or dry stop line reads *"TOO SHORT"*.

### The FAP, the printer and the ATC mailbox {#a350-windows}

**Input Ctrl+Shift+F** opens the list of the A350's other windows, as it does on the Fenix: **FAP**, **Mailbox** and **Printer**. **Enter** opens one, **Escape** closes the list.

**The FAP** is the flight attendant panel, read and pressed as a sighted pilot uses it, in the same kind of window as the OIS. It starts with its screen off: press *Wake the screen*.

- **The tabs** are Cabin Status, Lights, Temp, Doors/Slides and Water/Waste. Cabin Status also has a button for each of those pages; *Smoke Detect* has no page on this aircraft.
- **Cabin Ready** tells the cabin is ready: the button says On or Off, and the ECAM shows it. **Screen Off** turns the panel's screen off, on every page.
- **Lights:** **Zones** lists Cabin, Avionics Bay and Cargo Bay, each with its level. Press a zone, then Bright, Medium Dim, Low Dim or Off; the key at the zone's level says *current*. **Night Mode** and **Main On** set the cabin. The panel reads a level again when you come back to its page, and shows anything other than Off as Medium Dim then, for a sighted pilot too.
- **Temp:** each zone's temperature, front to back, and the requested temperature with its up and down keys, from 18 to 30 °C. With the overhead **Cabin temperature** knob away from its lowest position the panel says *"Temperature controlled by flightdeck"* in place of the keys.
- **Doors/Slides:** every door, with its state and colour: locked with the slide armed (blue), locked with the slide disarmed (green), moving (amber), open (red). **Enter** opens or closes it; the panel refuses while the aircraft moves or an engine runs, and says so.
- **Water/Waste:** each waste tank and the water tank in percent, with the colour its bar is painted (amber, then red, as a tank nears its limit), and **Refill Water** and **Empty Waste**, which work on the ground.

**The printer** window holds what the printer last printed, to read line by line, and shows a new printout as it arrives; **F5** reads it now, **Escape** closes. Printouts come from the MFD (ACTIVE, DATA, PRINTER: its PRINT keys and AUTO PRINT check boxes) and the OIS. The **Cockpit** panel's **Printer paper tear off** takes the paper off the printer and puts it beside the seat, and the **Misc** panel's **Torn paper put away** removes it; each has a status line.

**The ATC mailbox** (**Input Ctrl+Shift+D**, the A32NX's DCDU key, or the list above) is where the A350 shows its datalink messages, above the warning display. It lists the message shown, line by line, with its time and station, how many messages there are (*"MSG 1/2"*), then its buttons: **Enter** presses one. A request you prepare on the MFD's ATC COM pages and pass over with XFR TO MAILBOX waits here with **SEND** and **CANCEL**; a message from ATC comes with its replies, such as WILCO, STANDBY and UNABLE. **SEND** and a reply go out to ATC, as they do for a sighted pilot. **PRINT** prints the message, and the arrows move between messages and pages. *"MAILBOX NOT AVAIL"* is what it shows for the first 20 seconds after power-up and whenever the bus feeding it has no power. Output Alt+E reads its lines too.

### Things to know {#things-to-know-5}

- **The A350's First Officer is not there yet.** Output Shift+C says *"No checklist for this aircraft."* Use the MFD's own checklists (Output Ctrl+Shift+C).
- **Surroundings window frequencies:** the A350 has no stock COM radio, so pressing Enter on a frequency in the Surroundings window tells you that this aircraft tunes its radios only through its radio management panels.
- **Pictures stay pictures.** The OIS charts and en-route map, and the tapes, attitude and map symbols on the PFD and ND, are graphics. BA reads their values from the aircraft where it can.
- **After an A350 update,** BA prepares the displays again (about 15 seconds). If a display stops reading after an update, report it.

<nav class="guide-nav" aria-label="Guide chapters" markdown="1">

- [Previous chapter: Networks and Integrations](networks-and-integrations.md)
- [Next chapter: Boeing Aircraft and the MD-11](boeing-aircraft-and-the-md-11.md)
- [Guide contents](index.md)

</nav>
