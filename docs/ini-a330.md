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
  Conventions (measured, encoded in the map's `positions`): a push-button's input 0 = ON (its
  L:var reads 1), a 3-position switch is numbered from the TOP (0 = top), a 2-position switch has
  1 = top. Never assume the L:var value equals the input value — use `A330ControlMap.InputFor`.
* **The panel map is GENERATED** — `tools/a330-gen/generate_a330_map.py` →
  `Resources/a330_control_map.json` (281 controls), from a differential probe that fired every
  input event and diffed all 1,936 L:vars. Regenerate it; never hand-edit. A control's `read` is
  an L:var, a stock SimVar (`A:NAME, unit`), or — where no L:var moves — its own input event
  (`B:NAME`).
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
* **MCDU — read from pixels:** there is no text export (`iniAirbusMCDU_1` answers
  ILLEGAL_OPERATION; `INI_MCDU_OPTION` changes nothing). Shift+M forces the pop-out
  (`A330CduPopout` with `CduPopoutProfile.A330`): instrument view 12 (captain MCDU head-on; F/O 13 —
  0-based), wait for the camera to stop moving, bring the sim to the foreground (Alt-tap,
  AttachThreadInput, SwitchToThisWindow, then WScript `AppActivate` — a 2024 window refuses the
  plain call from a background process), Right-Alt click the DARK GLASS found by flooding from the
  expected screen point (a text-colour search fails here: the menu page is mostly white/grey), 2
  rounds. Measured: 5/5 first-click successes from a background console starting in the pilot view,
  and Shift+M from the app with NO pop-out open created it and read the page; page keys, Page Up/Down
  and Alt shortcuts verified in the window. Grid `CduLayout.A330` (y0 30.2, pitch 23.8, advance 15.0,
  cap height 18.3 — must equal the trainer's `BASE_CAP`, pinned by a test — ink threshold 40: grey
  text peaks ~77, so a higher threshold loses it). Colour per character (`CduColour`); grey words are
  spoken "(unavailable)" (`CduLineText`). Glyphs: `Resources/a330-cdu-glyphs.bin`, trained in
  `tools/a330-cdu-ocr` (39 labelled pages; leave-one-page-out 99.79 % (4237/4246), residual
  confusions O/Q, R/P, long dash vs '='). The three F-PLN pages added 2026-10-17 taught `(`, `)` and
  the ditto `"` — before them "(T/C)" read "1T/C1" and a repeated speed read "." — retrain there,
  never patch the .bin. `A330CduScreenReaderTests` reads those three committed captures end to end.
* **MCDU keys:** write 1 to `INI_MCDU1_<KEY>`. Verified: DIR PROG PERF INIT DATA FPLAN RADNAV
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
* **Camera restore after the MCDU pop-out** is written, read back and repeated until it holds, from
  the UI thread: a single type+index write sent from the pop-out's pool thread was reported sent and
  left the camera on the MCDU close-up (live 2026-10-17), which silences the cockpit ambience.
* **Camera views (0-based):** ECAM 3, PFD/ND 6, EFB 4/5, captain MCDU 12, F/O MCDU 13, pedestal 22.
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

The section and panel order is the generator's `SECTIONS` list (`tools/a330-gen`), which follows the
app's other Airbus aircraft (the FlyByWire A32NX and the Fenix): **Overhead** (Electrical, ADIRS,
APU, Oxygen, Fire, Hydraulics, Fuel, Air Conditioning, Bleed Air, Pressurization, Ventilation,
Anti-Ice, Wipers, Signs, Interior Lights, Exterior Lights, Calls, GPWS, Flight Control Computers,
Cargo Smoke, Recorders, Engine Start), **Glareshield** (FCU, Flight Modes, Captain EFIS, First
Officer EFIS, Warnings), **Main Instrument Panel** (Landing Gear, Brakes, Standby Instrument,
Instrument Switching, Clocks, Display Brightness) and **Pedestal** (Flaps and Speedbrake, Parking
Brake, Engine Masters, ECAM, Weather Radar, Transponder and TCAS, Captain Radio Panel, Captain
Audio). The FCU, Flight Modes, Landing Gear, Flaps and Speedbrake and Parking Brake panels are filled
by the definition itself (`OWN_PANELS` in the generator); everything else comes from the map.

## Unverified (needs a flight)
* FMA words for each mode number (`fma.log` has the raw values).
* A/THR, AP1/AP2, LOC and APPR pulses do nothing on the ground — expected, but unproven in the air.
* Parking brake direction (`AIRLINER_PARKINGBRAKE` input 0 = set), landing-light position words,
  dome/wiper labels, several `B:` reads.
* MCDU OVERFLY (needs a flight plan).
* The First Officer's engine start and shutdown flows end to end (their writes are measured on the
  ground, see First Officer above).
* Altimeter STD in a climb or descent, and the first officer's knob (measured on the captain's, on
  the ground).

## Probe side effect left in the aircraft
* Probing left RMP 1's NAV backup ON (`INI_RMP1_ILS_STATE`/`VOR_STATE` = 1) and it would not
  toggle back from outside. Press NAV on the captain's RMP to clear it.
