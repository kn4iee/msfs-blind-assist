# a330-gen

Generates `MSFSBlindAssist/Resources/a330_control_map.json`, the iniBuilds A330 (MSFS 2024) panel
map, from `curation.json`. The generator holds no control data: it loads the curation file, checks
it and writes the map. Never hand-edit the JSON: change `curation.json` and rerun.

    python tools/a330-gen/generate_a330_map.py
    python -I -m unittest discover -s tools/a330-gen -p "test_*.py"

Python 3, standard library only. Every path resolves from the file's own location.

## Files

- `curation.json`: every control, in the map's order, and everything the checks need.
- `keys.txt`: every key the map has ever had, one a line. A key never disappears (see below).
- `data/input_events.tsv`: the simulator's input-event catalogue (captured 2026-11-27, plan 04 Task 1
  Step 2). First line: the capture's date and the simulator's version. Then one `name<TAB>hash` line
  per event. It is committed: when it is missing the generator stops with an error before writing
  anything.
- `unproven.txt`: GENERATED on every run, never hand-edited: one line per offered position whose
  `proven` is false, `key<TAB>value<TAB>label`, sorted by key then value, under a `#` header. A test
  checks that the committed file equals what the curation implies.
- `generate_a330_map.py`, `test_generate_a330_map.py`.

## curation.json

- `panel_order`: section name to the list of its panels, both in the map's order (the order of the
  app's other Airbus aircraft: the overhead first in the A32NX's order, then the glareshield, the main
  instrument panel and the pedestal).
- `own_panels`: panels the definition fills itself (the FCU, the flight modes, the flap, gear and
  parking brake controls). They are listed in `panel_order` and may be empty; any other panel with no
  control is an error.
- `controls`: an object in the map's order. Its id is the control's input event, as the simulator
  names it (`AIRLINER_...`); a control with no event (a read-out) uses its own key as the id. Fields:
  `key` (never changes), `section`, `panel`, `kind` (`select`, `button`, `continuous`, `status`),
  `mode` (`set`, `push`, `toggle`, `rpn`), `read`, `positions`, and any extra the map carries
  (`press`, `lamps`, `holdMs`, `rpn`, `notes`, `min`, `max`, `step`), written to the map in the
  curation's order.
  A `toggle` with `press` (and no `rpn`) is a push-button the aircraft toggles: a pick presses it
  once, with ONE set of its input event to `press`, and only when its `read` is known and differs from
  the pick. Nothing is sent after that set: every set to these events is a press, so a release would
  be a second press. A `release` field is refused. Its positions' `input` values drive no write; they
  stay for the record. A `toggle` with an `rpn` (the engine masters) writes that line with `{value}`.
  A position is `{ "value": what the read shows, "input": what to send, "name": the key of the
  position's name, "proven": bool }`. Until the aircraft's own strings are available (plan 04 Task 2
  Step 2) `name` is the read value as a string. `proven` is true only where an indication the control
  does not write itself followed the name (see `proof`). `proof` names that indication:
  `{ "kind": "ewd_memo" | "sd_page" | "lamp" | "simvar", "text": ..., "when": the position at which it
  shows, "measured": date }`. The map carries `proof` and each position's `proven`.
  Further optional fields, written to the map after all the others in this order: `guard` (the key
  of the guard control, the cover over this switch; the guard's own row stays, just before it),
  `state` (a button: the variable its press changes; absent when the press shows nothing readable),
  `unit` (a continuous control: `{ "name", "min", "max" }`, input `min..max` of the control maps
  linearly onto the unit's `min..max`; the aircraft's own range), `pressesToCycle` (a multi-press
  key: how many presses bring it back to where it began; none set yet), `proof`.
- `name_overrides`: key to `{ "label", "aircraft_says", "why" }`. Today every label is an override
  with `aircraft_says: null` and the reason "kept until control_names.json lands"; Task 2 Step 2
  takes labels from the aircraft's strings and keeps an override only where the aircraft's string is
  missing, names an action, or two controls of a panel would read alike.
- `position_overrides`: key to position name to `{ "label", "aircraft_says", "why" }`, the same way.
- `left_out`: input event to the reason the map does not offer it. Today these are the events the
  definition's own code presses (the FCU window, the baro knobs, the autobrake levels, the parking
  brake). The MCDU keys' events are added when the catalogue gives their names.
- `not_yet_offered`: events the catalogue has that no control or `left_out` entry accounts for yet.
  Accepted only while plan 04 is open (`PLAN_04_OPEN` in the generator); Task 5 empties it and
  removes the allowance. Empty now.
- `retired_keys`: key to the short commit that removed it and the reason. None so far.

## Checks (a failure names the offending event or key)

- Catalogue (`data/input_events.tsv`, which must exist): every event in it is a control, in `left_out` or
  in `not_yet_offered`; no event is both a control and left out (or in `not_yet_offered`); a control
  or left-out event the catalogue lacks is refused; every `left_out` entry has a reason.
- Keys: a key in `keys.txt` that no control carries must be in `retired_keys`.
- A key repeated inside one object of `curation.json` is refused when it loads (a plain JSON load
  would drop the first silently); a `name_overrides` or `position_overrides` entry for a key no
  control carries, or for a position name the control lacks, is refused.
- Duplicate keys, a control without a label, a position without a label, an empty panel that is not
  an own panel, and a control in a panel `panel_order` does not list are refused.

## Adding a control

Add its entry to `controls` at its place in the map's order, with a `name_overrides` row (and a
`position_overrides` row per position), add its key to `keys.txt`, regenerate, and run the tests. The
event must be in the catalogue. `keys.txt`'s guard and the 73 toggles' press also have C# tests
(`A330ControlMapTests`), since CI runs the C# suite and not these Python tests.

## Measurement history

The aircraft is a streamed, encrypted package, so nothing is read from its files. Every control is
one of the aircraft's own FS2024 input events (SimConnect EnumerateInputEvents, 649 of them). Reads
and positions were measured live on 2026-10-16 by a differential probe: set the input event, snapshot
all 1,936 INI_* L:vars the panel-state file names, record what moved, put it back. The raw output is
not kept.

Conventions the probe found (do not "correct" them by intuition):

- A push-button's input is the opposite of its L:var: input 0 = pushed in (on/auto, light off) and the
  L:var reads 1 (PACK 1, the batteries, generators, bleeds, hydraulic pumps, PRIMs/SECs). Superseded on
  2026-11-27 for 73 push-buttons (PACK 1, the generators, bleeds, hydraulic pumps and PRIMs/SECs among
  them): they toggle, each set of their input event is a press, and their input event's own value says
  nothing about their state (see below).
- A three-position switch numbers from the top: 0 = the top position (strobe ON, no-smoking ON, nose
  light T.O., annunciator light TEST); a two-position switch has 1 = the top (beacon ON).
- A momentary key (ECAM page keys, master warning/caution, calls, agents) ignores a raw value: it is
  pressed with the event's own `_Push` / `_Release` actions (`mode: push`).
- Where a switch moves no L:var at all (probe heat, the centre tank pumps, the wipers...), its read is
  the input event's own value (`B:EVENT`): the app subscribes to it.

Deliberately not probed (dangerous to toggle with engines running) and taken from their current
values and the HubHop preset set instead: engine masters, engine start selector, ADIRS, IR/ADR, fire
handles and agents, IDG, RAT, gear lever, parking brake. Their positions follow the conventions above.

Measured since:

- 2026-11-19, on the ground: the engine masters' Set takes the position by value (1 ON, 0 OFF, read on
  INI_MIXTURE_RATIOn_HANDLE; a repeated Set leaves it where it is) and the start selector's Set 2 / 1
  is IGN START / NORM; an engine 2 start with APU bleed reached ENGINE_2_AVAIL at N2 58.6 %. The
  masters were added 2026-10-28 for the First Officer's engine start; the write presses only when
  INI_MIXTURE_RATIOn_HANDLE differs from the pick, passing the pick as the Set's value.
- APU START is the aircraft's own start command, written directly (`mode: rpn`). Measured 2026-10-28:
  the input event's `_Push`/`_Release` reached nothing (pressed twice with the master ON and the flap
  open, the APU never started), while writing `1 (>L:INI_APU_START_BUTTON_CMD)` started it at once
  (the WASM consumes the command and latches INI_APU_START_BUTTON; again 2026-11-19: AVAIL about 63 s
  later). The START legend is that latch, never the command, which reads 0 again within a frame.
- 2026-11-22, in flight: INI_WX_PWS_SWITCH and the input event both read 1 while the E/WD showed the
  amber memo PRED W/S OFF; after writing 0 both read 0 and the memo went. So predictive windshear
  0 = Auto, 1 = Off. The control reads back what was written, so a read of its own variable never
  proves its names.
- 2026-11-27 21:29, on the ground (MSFS 2024 1.9.24.0, the A330-200 VIP (GE), external power on), by
  the panel sweep through the app's own route (SimConnect SetInputEvent by hash): 73 push-buttons
  toggle. Writing the same input value twice flipped the read back. Every one started with its input
  event's own value at 0, whatever the switch showed. Two patterns:
  - 64 of them: SetInputEvent(1) with the event at 0 flipped the read (in about 100 ms), and
    SetInputEvent(1) again, with the event at 1, flipped it back. The put-back then sent
    SetInputEvent(0) twice, and after the second the read was at its start. The put-back sends a second
    write only when the first did not leave the control at its start (its input event's own value and
    its read both back); which of the two was not back after the first is not recorded.
  - 9 of them (the six engine-feed fuel pumps, crew oxygen and both flight directors):
    SetInputEvent(0) with the event at 0 flipped the read, and again flipped it back. The put-back
    found them at their start and sent nothing. Nothing else was sent to them.
- 2026-11-27 23:05, the same flight, a trial of two of them (`--only A330_CPT_FD,A330_PACK1`): the
  press by value then built (SetInputEvent(1), a 150 ms hold, SetInputEvent(0)) left both reads
  where they were: `dead` on pack 1 (one of the 64) and on the captain's flight director (one of the
  9).

  The one model that fits every result: every SetInputEvent to these events is a press (one flip),
  whatever the value. It reads the 64's two sets of 1 and the 9's two sets of 0 as two flips, the
  64's put-back (two sets of 0) as two flips, and the 23:05 press (a set of 1, then a set of 0) as two
  flips that cancelled. It is a model: on each key only the parts above were measured. An earlier
  reading of the 64's put-back ("the first set of 0 changed nothing") overlooked the put-back's rule
  above, and the 23:05 trial rules it out. These controls are now `mode: toggle` with `press: 1` and
  no release: a press is ONE set of 1, sent only when the control's read is known and differs from
  the pick. Their `read` and their positions' value and name pairing are unchanged; their positions'
  `input` values drive no write. The one-set press itself has not met the simulator yet: the panel
  sweep's next run presses each of them once and back, which measures it.
