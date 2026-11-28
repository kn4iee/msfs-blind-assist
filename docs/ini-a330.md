# iniBuilds A330 (MSFS 2024)

The A330-200 that ships INSIDE Microsoft Flight Simulator 2024, built by iniBuilds (streamed,
encrypted package `fs24-microsoft-aircraft-a330`, titles "A330-200 (RR)" and "A330-200 (GE)").
Aircraft code `INI_A330`, `Aircraft/IniA330Definition*.cs` + `Aircraft/A330/` + `Forms/IniA330/`.
Its autoflight and systems report through iniBuilds' own `INI_*` L:vars; its cockpit switches are
FS2024 input events. Not to be confused with the Headwind A330-900neo (`HeadwindA330Definition`), a
different aircraft built on the FlyByWire A32NX.

## Mechanisms (each measured live, 2026-10-16)

* **Controls are FS2024 input events** (`B:AIRLINER_*`, 649 of them), not writable L:vars. The
  `INI_*` L:vars are the state the WASM reports. Two write forms, measured per control:
  * selectors and latching switches: `SetInputEvent(hash, value)` where the value is the POSITION;
  * momentary keys only react to the calculator form `(>B:NAME_Push)` / `(>B:NAME_Release)` —
    SetInputEvent with 1 then 0 does nothing on them.
  * push-buttons that toggle (73, found 2026-11-27: `mode: toggle` with `press` / `release` in
    the map): a pick presses once by value (set 1, the hold, set 0), only when the control's read
    differs from the pick. The model behind it, that every SetInputEvent is a press except a change
    from 1 to 0, fits what was measured; which part was measured on which keys is in
    `tools/a330-gen/README.md`. All 73 started with their input event's own value at 0, whatever the
    switch showed.
  Conventions (measured, encoded in the map's `positions`): a push-button's input 0 = ON (its
  L:var reads 1; for the push-buttons that toggle the input drives no write), a 3-position switch is
  numbered from the TOP (0 = top), a 2-position switch has 1 = top. Never assume the L:var value
  equals the input value — use `A330ControlMap.InputFor`.
* **The panel map is GENERATED** — `tools/a330-gen/generate_a330_map.py` →
  `Resources/a330_control_map.json` (281 controls), from a differential probe that fired every
  input event and diffed all 1,936 L:vars. Regenerate it; never hand-edit. A control's `read` is
  an L:var, a stock SimVar (`A:NAME, unit`), or — where no L:var moves — its own input event
  (`B:NAME`).
* **What a pick and a press on a panel control do** (plan 04, `IniA330Definition.Panels.cs`, the write
  decision in `A330Write`, the units in `A330Units`):
  * A selector pick reads the control first and writes only when the read differs. It is read back
    after 1.5 s (`ReadBackDelayMs`, a named constant until the sweep measures settle times; no
    settle in the 2026-11-27 sweep took longer than 1.5 s) and its row is resynced to what the aircraft shows.
  * A refused write is said once and never reported as done. A display window's press of a refused
    control returns false (`PressFromWindow`). The panel still reports the pick as handled, so the app
    never falls back to writing the read L:var itself ([INI-1]).
  * A toggle with no by-value write is never pressed while its read is unknown.
  * A key (a momentary button) with a state variable reads it back once after its press and says it, in
    the A32NX's form (the bare state, such as an ECAM page's name); a key with none says "<name> pressed".
  * The key state rows (ECAM page, selected radio, master warning and caution, APU START) are announced
    by the definition itself, with its own baseline and a quiet start of 5 s (`StartGraceMs`, the same
    as MainForm's, which these rows never reach). The master warning and caution variable names are
    unmeasured guesses; a wrong one reads nothing and the row stays silent.
  * Weather radar tilt is a combo in degrees, -15 to +15, from the input's 0 to 30 (`A330Units`, the
    A32NX's and the Fenix's wording for a signed step). The temperature and landing elevation sliders
    still say 0 to 100 until their ranges are measured.
  * Lamps and status rows say On and Off; the parking brake says On and Off; the autobrake says LO, MED
    and MAX.
  * The speedbrake lever is said in the A32NX's 10 % bands, with a Ctrl+M row. It stays quiet while it
    travels to the pilot's own pick (`SpeedbrakePickQuietMs`, 3 s, unmeasured).
* **Input-event READS** are a generic SimConnectManager feature (`SimVarType.InputEvent`,
  `SimConnectManager.InputEventReads.cs`): `SubscribeInputEvent` + `GetInputEvent`, request ids
  701–890 rotating. They are skipped by data-definition registration and the continuous batch
  (they cost no data-def slot) and are re-subscribed after every input-event enumeration and on an
  Aircraft-menu switch (`RefreshInputEventReads`).
* **FCU:** writing the dial L:vars directly works (`INI_HEADING_DIAL` 270 → AP heading 270).
  Pulses: `INI_FCU_SELECTED/MANAGED_SPEED/HEADING_BUTTON`,
  `INI_FCU_ALTITUDE_PULL/PUSH_COMMAND`, `INI_AP1/AP2_BUTTON`, `INI_LOCALIZER_BUTTON`,
  `INI_APPROACH_BUTTON`, `INI_FCU_HDG_VS_COMMAND` (TRK/FPA). V/S pull = SetInputEvent
  `AIRLINER_MCU_VS_PULL` 1 then 0. SPD/MACH and METRIC are input events.
* **Flight modes:** the mode numbers in the `INI_*` FMA L:vars, spoken with A330 words (`A330Fma`).
  Six numbers are measured on this aircraft (on the ground, listed in `A330Fma`); every other number
  in its tables is not yet seen on the A330 and needs a flight. An unknown number is spoken as
  "vertical mode N", never guessed; every change is logged raw to `fma.log` so the words can be
  checked after a flight.
* **FCU dialogs** (Ctrl+H/S/A/V) carry every button beside that knob on the real FCU, each showing
  its live state: heading — pull, push, LOC, HDG-V/S / TRK-FPA; speed — pull, push, SPD/MACH, A/THR;
  altitude — pull, push, ALT, knob steps (100/1000 ft), metric, AP 1, AP 2; V/S — pull, push,
  HDG-V/S / TRK-FPA, APPR. No EXPED (this aircraft has no such input event). Live-verified: all four
  open with their buttons; the knob-steps button drives the aircraft and its label follows.
* **ECAM pages** (`INI_ECAM_ACTIVE_PAGE`): ENG 1, HYD 2, EL/AC 3, EL/DC 4, BLEED 5, COND 6,
  PRESS 7, FUEL 8, APU 9, F/CTL 10, DOOR 11, WHEEL 12, STS 14.
* **MCDU — from the display reader (plan 03, 2026-11-22):** the window (Shift+M) shows the page from the
  display reader's frames (`IA330Displays`, plan 02), both MCDUs (Ctrl+Shift+L / Ctrl+Shift+R, as the Fenix
  window): no pop-out, no camera move, no focus change. A frame becomes the 14 rows of 24 cells by where each
  text's first character sits (`A330McduScreen`, `A330McduGeometry`: Left 22 + column x 16.87, Top 43 + row x
  25.85, nearest cell); a text starting off that grid refuses the page. Special codes show as one cell each
  (`A330McduCharacters`: box `-`, degree, arrows, overfly mark); a row keeps its 24 columns for braille and is
  listed as drawn: no word for greyed text, no colour, a nameless F-PLN row's name cells blank. As in the Fenix and
  FlyByWire windows the first line reads `Title: ` and the title, with a page counter drawn half a row under the
  title (P/N XLOAD's `1/7`) on it. The window says what the Fenix and FlyByWire windows say, in their words, and
  nothing more (owner, 2026-11-23): a title whose words differ from the last one said (opening the window too; the
  slew arrows and the page counter on the title line are listed but never count, as on the FlyByWire window), the
  selection then on line 1; the scratchpad line when it changes; the side; "MCDU disconnected" / "MCDU connected"
  (the reason in the "Connection status" box, which reads "MCDU: Connected" over a page). A line key, a slew,
  AIRPORT and CLR say nothing of their own; Left and Right on the screen turn the page, as on those windows. Its
  controls carry their names ("MCDU selector", "MCDU Display", "MCDU Input"). Typing waits for the MCDU's own typed
  line (600 ms a key at most, 150 ms pace) and says it once; an FMS message on the captain's scratchpad line is spoken with the window
  closed, the message alone as the A32NX speaks one (Ctrl+M row "FMS messages"). Since its build of 2026-11-22 the
  display reader gives the MCDU's colours, leaves out the list rows the MCDU hides, and says whether a list has rows
  hidden above or below (`A330Frame.List`, `ListAbove`, `ListBelow`). It sends each new frame once without them
  (moments later, with them), and now and then a frame with a text off the grid (logged once, with the text and its
  place): the window keeps the page it shows meanwhile, for up to 3 s after the last page read,
  then lists only "Reading the captain's MCDU" until a page comes, and takes no message from the scratchpad line of a
  frame without them. The slew keys are never sent past a list's top or end, silently (a press at a list's end does
  nothing, as on the other windows): the A330 counts such presses and the list then seems stuck (seen by the owner on
  ARRIVAL, 2026-11-22); a slew is judged only on a page, waiting up to 1.5 s for one. There is no AI read of the
  MCDU. The pixel reader the window used before (a forced pop-out read by a trained glyph matcher, and its training
  tool) was removed on 2026-11-23 once the owner had checked the new window; it is described in this file's history.
* **MCDU keys:** write 1 to `INI_MCDU1_<KEY>` (the first officer's: `INI_MCDU2_<KEY>`). Verified: DIR PROG PERF INIT DATA FPLAN RADNAV
  AIRPORT MENU FUEL SEC ATC PREV NEXT, letters, digits. Slew keys verified in flight 2026-10-17:
  `UARROW` moves on to later rows (down the F-PLN, a screen down a list), `DOWN` back toward the top.
  The window sends them key for key, as the FlyByWire A320 and Fenix windows do: Page Up and Alt+Up
  send UARROW, Page Down and Alt+Down send DOWN (the owner's ruling, 2026-11-20; until then this window
  had the two swapped). Measured 2026-11-20 on the ground: DECIMAL, SLASH and SPACE type their character;
  PLUS is the +/- key (first press a minus, second press turns it into a plus); CLR deletes a character,
  or a queued message first; OVERFLY marks and unmarks a waypoint. AIRPORT is not a page: it moves the
  flight plan to the destination, then the alternate, then back to the origin.
* **EFB:** Coherent HTML page "iniEfbA330", `Resources/coherent-a330-efb-agent.js` +
  `CoherentPmdgEfbClient.ForIniA330()`. Side-bar buttons, door state from the
  `door-open`/`door-transit` class or the tile's gradient, the GPU from `INI_GPU_AVAIL`. The Chocks,
  Engine Covers and Pitot Covers buttons read what the EFB's own code reads: in MSFS 2024
  (`L:IS2024` = 1) the SIMULATOR's chocks and covers, `COVER ON:0`, `:1` and `:2` (see below);
  otherwise the aircraft's own `INI_CHOCKS_ENABLED` / `INI_COVERS_ENABLED`, which read 0 in 2024
  even with all three on. The two cover buttons share one element id, so the label tells them apart.
  Live-verified scrape + click (Ground tab) and the Payload page. `A.FIELDS` must have ONE key per
  element id — an object literal keeps the last duplicate, and a stray second `zfw` entry renamed the
  cargo slider "Zero fuel weight". Units come from the page's unit box (kg or lb), never from the
  field name.
* **Camera views (0-based):** ECAM 3, PFD/ND 6, EFB 4/5, captain MCDU 12, F/O MCDU 13, pedestal 22. (The MCDU's
  views were the removed pixel reader's; the MCDU window moves no camera.)
* **MSFS 2024 chocks and covers** (measured 2026-11-19, A330-200 (GE) loaded cold and dark): the
  simulator's own wheel chocks, engine covers and pitot covers (`COVER ON:0`, `:1`, `:2`) were all on
  at the start. With the engine covers on, an engine 2 start (APU bleed, IGN START, master ON) held N2
  at 0 % for 60 s with the starter engaged; with them off, the same start lit at about 12 s and reached
  `ENGINE_2_AVAIL` at N2 58.6 %. The EFB's Ground page shows these three, but its buttons do not
  remove them: in 2024 they send a one-number `K:COVER_SET:n` (recorded from the page on a real
  mouse press of Chocks: `K:COVER_SET:0` = 0), which the simulator ignores, so a sighted pilot
  cannot remove them from this tablet either. What works: writing the state itself from the EFB's
  page (`A:COVER ON:n` = 0 or 1, all three, both ways); `K:COVER_SET` with no index and the target
  state (the chocks only: MSFS 2024's own Ground Services app sends exactly this); and over the
  calculator `<state> <index> (>K:2:COVER_SET)` (`0 0` chocks off, `1 0` on, `0 1` / `1 1` the engine
  covers). `TRIGGER_KEY_EVENT` is not bound in the EFB's page.
* **BA's own chock and cover presses — TEMPORARY ([INI-9]):** while the agent's `A.COVER_REMOVAL` is
  true, pressing Chocks, Engine Covers or Pitot Covers in BA's A330 EFB window writes that cover's
  `A:COVER ON:n` itself (`A.coverPress`, ground only) instead of clicking the EFB's own dead button,
  and the row reads back its new state; the EFB's own "Chocks Removed" / "Covers Set" notices also
  show. Live-tested 2026-11-19 through the agent's press path: all three off and back on. Once BA's
  simulator tablet removes chocks and covers for every aircraft, switch it off: the three rows then
  read as status lines (`static`, not clickable), never as buttons that do nothing.
* **Altimeter STD** (measured 2026-11-19 on the ground, powered, captain's knob): the baro knob's
  input event pressed BY VALUE (`AIRLINER_CPT_BARO_SET_PULL` set 1 then 0) turns `XMLVAR_Baro1_Mode`
  0 → 3 (STD), the PUSH the same way 3 → 0 (QNH); the event's `_Push`/`_Release` action does
  nothing. The Standard button tries the value press first, then the action and the
  `INI_*_ALTIMETER_*_COMMAND` L:vars as fallbacks, and logs which one worked.
* **First Officer (Shift+C):** `FirstOfficer/IniAirbus/` (profile, flows, checklists and the key
  table `IniAirbusKeys`); it writes only through this definition's `HandleUIVariableSet`, starts and
  shuts down the engines itself (masters and mode selector, since 2026-10-28) and leaves the ADIRS,
  flaps, spoilers, parking brake and altimeter STD to the Captain. The engine masters' input event
  `AIRLINER_ENGn_CUTOFF` takes the position by value (measured 2026-11-19: Set 1 ON, Set 0 OFF on
  `INI_MIXTURE_RATIOn_HANDLE`, a repeat leaves it), and the start selector's 2 / 1 is IGN START /
  NORM; a master is still pressed only when the read differs.

## Panel layout

Where the panels come from: `tools/a330-gen/curation.json` is read by the generator
(`tools/a330-gen/generate_a330_map.py`), which writes `MSFSBlindAssist/Resources/a330_control_map.json`.
Never hand-edit the map ([INI-2]). The curation file holds every control's kind, panel, label overrides,
guard, state read and proof, the panel order, and what is left out and why. Besides it, the generator
has three data inputs, and two of them are not in the repository yet:

* `tools/a330-gen/data/input_events.tsv`: the simulator's own catalogue of the aircraft's input events
  (649, captured 2026-11-27). Present.
* `tools/a330-gen/data/control_names.json`: the aircraft's own names for its controls. Not present: it
  needs the private reference's export. Until it lands, today's labels are the generator's own, kept
  as overrides in the curation file.
* `tools/a330-gen/data/procedure_order.json`: the order source, to come with plan 04 Task 6. Not present.

**Sections and panels.** The order is the curation's `panel_order`, which follows the app's other
Airbus aircraft (the FlyByWire A32NX and the Fenix): **Overhead** (Electrical, ADIRS,
APU, Oxygen, Fire, Hydraulics, Fuel, Air Conditioning, Bleed Air, Pressurization, Ventilation,
Anti-Ice, Wipers, Signs, Interior Lights, Exterior Lights, Calls, GPWS, Flight Control Computers,
Cargo Smoke, Recorders, Engine Start), **Glareshield** (FCU, Flight Modes, Captain EFIS, First
Officer EFIS, Warnings), **Main Instrument Panel** (Landing Gear, Brakes, Standby Instrument,
Instrument Switching, Clocks, Display Brightness) and **Pedestal** (Flaps and Speedbrake, Parking
Brake, Engine Masters, ECAM, Weather Radar, Transponder and TCAS, Captain Radio Panel, Captain
Audio). The definition fills five panels itself (the curation's `own_panels`): FCU, Flight Modes,
Landing Gear, Flaps and Speedbrake and Parking Brake. Everything else comes from the map.

**What is left out, and why.** The curation's `left_out` holds 32 input events, each with its reason. By
group: the FCU window's own buttons, knobs and dials (21: it drives them through press flags, dial
variables and its own presses); the altimeter knobs of both sides (4, pressed by the definition's baro
knob); the autobrake levels (3, pressed by the definition's autobrake row); the parking brake (1, set by
the definition's own control); and the flap, speedbrake and gear levers (3, driven through stock events
by the definition's panels). Apart from these, `not_yet_offered` lists 346 catalogued events that have
no row yet (plan 04 Task 5 works through them); `docs/a330/evidence/04-panels.md` lists them by group.

**`keys.txt`.** `tools/a330-gen/keys.txt` is the committed list of every control's key (281). A key never
changes or disappears: Ctrl+M settings, the First Officer (`IniAirbusKeys`), the FCU plan and the display
windows hold them. A test fails when a regenerated map lacks one.

**`unproven.txt`.** `tools/a330-gen/unproven.txt` lists every position (key, value, label) not yet proven by
an indication the control does not write itself (an E/WD memo, a lower ECAM page, a lamp, or a simulator
variable the aircraft drives). A control's own read proves nothing here: it reads back what was written.
Today only predictive windshear is proven (2026-11-22, in flight); every other pairing of a position's
name with its value is unproven.

**The naming rule** (until the owner rules, plan 04 Task 3): sentence case, the aircraft's abbreviations
as it writes them, a space before a digit that follows letters, and the side first where two rows of a
panel would read alike ("Captain ...", "First officer ..."). Today's labels are still the generator's own,
kept as overrides until the names file lands; the generator does not apply this rule yet.

**The order rule** (to come, plan 04 Task 6): what every flight uses first, in the order the simulator's
procedures first use it; then what is set once; then non-normal and maintenance controls. Today's order
is the cockpit's panels in the A32NX's order.

**The panel sweep.** `tools/a330-gen/PanelSweep` (its README) is a live console tool, not part of the
solution. It drives each planned control through the app's own write decision and routes, reads each
back until it settles, and records a verdict a row; it journals every starting value first and can put
them back (`--restore`). Its deny list keeps it off what cannot be undone or what moves the aircraft.
It ran once, on 2026-11-27 (on the ground, engines off, external power on, 151 units): 172 read-only,
73 toggle, 10 dead, 2 already there; none lagged and no SimConnect call threw. Its main finding is the
push-buttons that toggle (see Mechanisms). What it cannot prove: a position proved by a display (an
E/WD memo, an ECAM page) needs the display reader, which the tool does not use, so those stay
unproven; and a read-only verdict is not a proof. The results, the dead controls and what was not put
back are in `docs/a330/evidence/04-panels.md`.

## Displays

The cockpit displays' text comes from the display reader, a separate program (`A330DisplayReader.exe`)
placed beside the app and built from the team's private reference. Nothing in this repository reads the
displays itself; the app only receives frames (`Aircraft/A330/Display/`, measured in
`docs/a330/evidence/02-reader-client.md` while that folder exists).

* **Lifetime.** `MainForm.IniBuilds.Displays.cs` starts it with the A330 (`StartDisplays`); a switch away
  and the app's exit stop it (`StopReader`); a flight load or a dropped connection sends it `reset`
  (`ResetReader(true)`). It runs without a window in a kill-on-close job, so it ends with the app even
  on a crash, and each app instance has its own. If it exits by itself, or does not answer the app's
  `hello` within 5 s, the client starts it again after 5 s, at most three times in five minutes, then
  reports `Failed`.
* **Protocol** (`A330ReaderProtocol`, version 1): one JSON object a line on its standard input and
  output. The app sends `hello`, `want` (a display and a refresh), `release`, `reset` and `quit`; the
  reader sends its `hello` with its version (another version is `OutOfDate`), its state, frames and the
  MCDU's scratchpad. Nothing but a `hello` is taken before the reader's `hello`. A line the app cannot
  parse is dropped and the first of a run is logged by its length and first key only, never its text. The
  reader's standard error goes to `a330-displays.log`, so it must never write what a pilot typed there.
* **What consumers see** (`IniA330Definition.Displays`, an `IA330Displays`): a frame is a display's drawn
  texts with positions, fonts and, once the reader gives them, colours (spec M14), marked complete or not,
  drawn or not, and whether hidden list rows were left out (spec M15). `Latest` is the newest complete
  frame no older than three refreshes; only it is shown or spoken. `Newest` is any frame, for "reading"
  and "is off". Each owner `Want`s a display with a refresh (250 ms while a window shows it, 1000 ms in
  the background) and `Release`s it; the reader is asked for the smallest. The reader reports `Running`
  only once a display is wanted and found, so with nothing wanted the state stays `Starting`.
* **States in the pilot's words** (`A330ReaderStatus`): "A330 displays starting" and "waiting for the
  aircraft" pass and are never spoken; "unavailable: the display reader is not installed", "no access to
  the simulator", "this simulator version is not recognised yet", "the display reader is out of date" and
  "the display reader stopped" last. A lasting one is spoken once, 30 s after the aircraft became active
  at the earliest, with "The MCDU window cannot read its screen." (`A330ReaderNotice`, through the
  non-interrupting announce); "A330 displays ready" follows once if it then reads.
* **The display window** (`Forms/IniA330/A330DisplayWindow.cs`), the base of every display window: the
  list of `Rows(Latest)`, kept in place every refresh; without a complete frame one status row (the state's
  words, "<Display> is off", "Reading the <display>", and after 30 s "The display reader cannot read the
  <display>"); "Colours not read yet" and "List rows not read yet" above the rows for a window that needs
  them; the cockpit controls under the list (`A330KeyRow`: a button or a list of positions, labelled with
  the control's state, pressed through `PressFromWindow`). After a gap in frames the pilot's row is kept.
  A derived window may list rows of its own while no complete frame has come (`RowsWithoutFrame`), and its
  `Rows` leaves out anything that could be a hidden list row while `HiddenRowsLeftOut` is false. F5
  refreshes, Escape closes; the window never speaks.
* **Tests** build on captured frames (`tests/MSFSBlindAssist.Tests/A330/Fixtures/p02_*.json`, each a
  frame in the protocol's form and nothing else) through the replay source `A330ReplayDisplays`.

## Unverified (needs a flight)
* FMA words for each mode number (`fma.log` has the raw values).
* A/THR, AP1/AP2, LOC and APPR pulses do nothing on the ground — expected, but unproven in the air.
* Parking brake direction (`AIRLINER_PARKINGBRAKE` input 0 = set), landing-light position words,
  dome/wiper labels, several `B:` reads.
* Every panel position except predictive windshear is unproven (`tools/a330-gen/unproven.txt`): the
  2026-11-27 sweep proved no position by an independent indication, only that each read moves (or
  does not) when written.
* MCDU OVERFLY (needs a flight plan).
* The First Officer's engine start and shutdown flows end to end (their writes are measured on the
  ground, see First Officer above).
* Altimeter STD in a climb or descent, and the first officer's knob (measured on the captain's, on
  the ground).

## Probe side effect left in the aircraft
* Probing left RMP 1's NAV backup ON (`INI_RMP1_ILS_STATE`/`VOR_STATE` = 1) and it would not
  toggle back from outside. Press NAV on the captain's RMP to clear it.
