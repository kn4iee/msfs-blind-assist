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
- `data/input_events.tsv`: the simulator's input-event catalogue (not captured yet: plan 04 Task 1
  Step 2). First line: the capture's date and the simulator's version. Then one `name<TAB>hash` line
  per event. While it is absent the catalogue check is skipped (the test says so).
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
  (`lamps`, `holdMs`, `rpn`, `notes`, `min`, `max`, `step`), written to the map in this order.
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

- Catalogue (when `data/input_events.tsv` exists): every event in it is a control, in `left_out` or
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
`position_overrides` row per position), add its key to `keys.txt`, regenerate, and run the tests. If
the catalogue is present, the event must be in it.

## Measurement history

The aircraft is a streamed, encrypted package, so nothing is read from its files. Every control is
one of the aircraft's own FS2024 input events (SimConnect EnumerateInputEvents, 649 of them). Reads
and positions were measured live on 2026-10-16 by a differential probe: set the input event, snapshot
all 1,936 INI_* L:vars the panel-state file names, record what moved, put it back. The raw output is
not kept.

Conventions the probe found (do not "correct" them by intuition):

- A push-button's input is the opposite of its L:var: input 0 = pushed in (on/auto, light off) and the
  L:var reads 1 (PACK 1, the batteries, generators, bleeds, hydraulic pumps, PRIMs/SECs).
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
