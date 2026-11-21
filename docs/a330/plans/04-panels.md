# A330 Panels Implementation Plan (plan 04, early)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** Every control the cockpit has is either in BA's panels or listed as left out with its reason; every control's name and position names are the aircraft's own; every control has been driven and read back once, and what was dead, inverted or mislabeled is fixed; the missing panels exist; and inside each panel the controls a pilot uses on every flight come first, in the order the simulator's own procedures first use them.

**Architecture:** The generator stops carrying hand-written names. It gets three inputs: the simulator's list of the aircraft's input events (captured once, committed), a data file of the aircraft's own names for its controls (exported by the private reference), and a curation file that says, per control, its panel, how it is written and read, which value each position name belongs to, and its place in the order; plus every event left out and why. A sweep tool drives every control through the app's own write path and reads it back. The order comes from the simulator's own procedures: the private reference captures them and hands over only the order of BA's own controls.

**Tech Stack:** Python 3 standard library (`tools/a330-gen`), C# 13 / .NET 10, xUnit; a C# console sweep modelled on `tools/b757-gen/PanelSweep`.

**Spec:** `docs/a330/spec.md` sections 2.2, 3 (R8-R11), 4 ("Sources for names and order"), 6 (M5, M6); `docs/ini-a330.md` "Panel layout"; rules [INI-1], [INI-2], [VAR-1].

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply. [INI-1]: a position's input value is never assumed equal to its read value. [INI-2]: the map is generated, never hand-edited.
- Owned files: `Aircraft/IniA330Definition.cs`, `Aircraft/IniA330Definition.Panels.cs`, `Aircraft/A330/A330ControlMap.cs`, `Resources/a330_control_map.json`, `tools/a330-gen/**`, tests `tests/MSFSBlindAssist.Tests/A330/A330ControlMapTests.cs`, `A330Panel*Tests.cs`, `tools/a330-gen/test_*.py`; `docs/ini-a330.md` "Panel layout" and its controls bullets; `docs/a330/evidence/04-panels.md` (new).
- **A control's key never changes.** Ctrl+M settings, the First Officer (`IniAirbusKeys`), the FCU plan and the display windows hold keys. A control that must change its key is agreed with the claimants of plans 05, 06, 08 and 09 first and listed in the handoff.
- Names and position names come only from the aircraft's own strings, as `tools/a330-gen/data/control_names.json` carries them. An override is allowed only where the aircraft's string is missing, names an action instead of a state ("PRESS"), or two controls of one panel would read alike ([VAR-1]); each override is in the curation file with the aircraft's own string beside it and the reason.
- The generator reads no file of the aircraft's package. Names reach it only through `control_names.json`, and order only through `procedure_order.json`; both are written by the private reference's export and hold nothing else (a test checks their shape). The simulator's checklist text may be committed or quoted where it helps (spec D13, owner 2026-11-21); the order still comes only from `procedure_order.json`.
- The sweep never drives what cannot be undone or what moves the aircraft (its deny list, Task 4). Engines off, on the ground, the owner present.
- Tests never depend on the generated map's labels (in-memory rows); a test may depend on keys and on the curation file's order.

## Measured on 2026-11-20

- **Names are public (spec D13, the owner's answer of 2026-11-20):** `control_names.json` (each control's title and position names) and the aircraft's variable names are committed in this repository, as the other aircraft's maps are. Only the simulator's checklist text and the reader stay private.
- **M6 is measured (Task 6 Step 1's capture is done).** MSFS 2024's own tablet (Aircraft, Checklists)
  lists the aircraft's 38 procedures in order, in three groups (Preflight, Inflight, Postflight), 669
  items, and the app's sim tablet reader opens and reads it with nobody touching the simulator. The
  capture is with the private reference. What is left of Step 1 is turning it into
  `procedure_order.json`: for each procedure, this app's control keys in the order its items name them.
- The names of every variable the aircraft uses (about 2,600) were listed the same day, also private:
  whoever has access checks a control's read against it before calling a control unreadable.
- Measured on single controls: the parking brake's input event is 0 = set, 1 = released; the ADIRS mode
  selectors take 1 = NAV by value; the battery push-buttons take 0 = AUTO; external power A takes 1 = on;
  an ECAM page key works as `_Push` then `_Release`; the ND mode and range selectors take their position
  by value; the first officer's altimeter knob is pressed by value, as the captain's.
- Keys that change nothing: the ECAM's C/B key (the page stays), and STS with nothing to report.

## Review Focus

- An input event in the catalogue that is neither a control nor left out: the generator fails, naming it (Task 2 test).
- A position name paired with the wrong value (the aircraft counts some selectors from the top, some from 1): each pairing is marked proven or unproven, and an unproven one is listed for the owner, never shipped as fact (Task 2, Task 4).
- Two rows of one panel with one name after the aircraft's titles are applied (the captain's and first officer's copies): the side comes first in the name, and the all-aircraft sweep of plan 01 still passes (Task 3 test).
- A regenerated map that drops or renames a key silently: a test compares the key set with the committed list of keys and fails on a removal (Task 2 test).
- A control whose read is its own input event and that reads back what was written even when the aircraft ignored it ([DBG-5]): the sweep checks a downstream variable where one exists and marks the rest "read is its own event" (Task 4).

---

### Task 0 [controller]: Claim this plan

As plan 01's Task 0, with this file's path and `docs(a330): claim plan 04` (`Plan: 04`). Branch from the base commit the index records for early plans.

### Task 1 [sim]: The catalogue and the aircraft's own names (M5)

**Files:** Create `tools/a330-gen/data/input_events.tsv`, `tools/a330-gen/data/control_names.json`, `tools/a330-gen/test_data_files.py`, `docs/a330/evidence/04-panels.md`.

**Interfaces:**
- Produces `control_names.json`: `{ "source": { "date": "...", "simulator": "..." }, "controls": { "ADIRS1_MODE": { "title": "ADIRS1 IR MODE", "positions": { "0": "OFF", "1": "NAV", "2": "ATT" } } } }`. A control's key is its input event's name without `AIRLINER_`; a position's key is the aircraft's own (a number, or `ON` / `OFF`).

- [ ] **Step 1:** The names file: someone with access runs the private reference's export and commits its output here unchanged. Steps 1-5 for its shape: `test_control_names_holds_only_titles_and_position_names` (no other key; every value a string of at most 60 characters), `test_every_control_has_a_title`; commit `feat(a330-gen): the aircraft's own control names`.
- [ ] **Step 2:** The simulator's list: with the A330 loaded, start a Debug build of MSFSBA once (the owner's go-ahead: it opens the app), select the A330, and copy the A330's block from `%APPDATA%\MSFSBlindAssist\logs\input_events.txt` to `tools/a330-gen/data/input_events.tsv` (name, tab, hash; a first line with the date and the simulator's version). Commit it.
- [ ] **Step 3:** In the evidence file, one table: every catalogued event, the aircraft's title for it (or "no name"), and whether today's map has it. Then the same by group, with counts. This is the list Task 5 works through.

### Task 2: The generator on data, with nothing unaccounted for

**Files:** Create `tools/a330-gen/curation.json`, `tools/a330-gen/keys.txt`, `tools/a330-gen/test_generate_a330_map.py`; modify `tools/a330-gen/generate_a330_map.py`, `tools/a330-gen/README.md`; `Resources/a330_control_map.json` (regenerated).

**Interfaces:**
- Produces `curation.json`:
  ```json
  {
    "controls": { "<EVENT or own key>": { "key": "A330_IRS1_MODE", "section": "Overhead", "panel": "ADIRS", "kind": "select",
                  "mode": "set", "read": "INI_IRS1_STATE",
                  "positions": [ { "value": 0, "input": 0, "name": "0", "proven": true }, ... ],
                  "lamps": [ ... ], "holdMs": 150 } },
    "name_overrides": { "A330_IR1": { "label": "...", "aircraft_says": "IR1", "why": "..." } },
    "position_overrides": { "A330_IR1": { "1": { "label": "On", "aircraft_says": "PRESS", "why": "the aircraft's string names the action" } } },
    "left_out": { "CPT_MCDU_A": "pressed in the MCDU window through its own key flags", ... },
    "panel_order": { "Overhead": ["Electrical", ...] },
    "control_order": { "ADIRS": ["A330_IRS1_MODE", ...] }
  }
  ```
  `"name": "0"` is the key of the aircraft's string for that position; the generator writes the string's text as the label. `keys.txt` is every key the map has ever had, one a line.

- [ ] **Step 1:** Move what `generate_a330_map.py` holds as Python calls into `curation.json` (a one-off script, not committed), keeping today's labels as overrides for now, so the regenerated map is byte-identical to the committed one. Tests: `test_the_map_regenerates_identically_from_curation`, `test_every_catalogued_event_is_a_control_or_left_out_with_a_reason`, `test_a_left_out_event_that_is_also_a_control_is_refused`, `test_no_key_ever_disappears` (against `keys.txt`), `test_a_control_naming_an_event_the_catalogue_lacks_is_refused`. The second test fails now: fill `left_out` only for the events the definition presses itself (the FCU, the brakes, the baro knobs) and the MCDU keys, and put every other event in a `not_yet_offered` list, which the test accepts only while this plan is open (Task 5 empties and removes it). Commit `refactor(a330-gen): the map from a curation file; every event accounted for`.
- [ ] **Step 2:** Names from the aircraft: the generator takes each control's label from its title in `control_names.json` and each position's label from its position name there, applying the naming rule Task 3 sets, unless an override says otherwise. Tests: `test_a_label_is_the_aircrafts_title`, `test_an_override_without_the_aircrafts_string_and_a_reason_is_refused`, `test_an_action_word_is_never_a_position_name` ("PRESS", "PUSH", "PULL", "TURN" need an override), `test_an_unproven_pairing_is_listed`. Regenerate; list every label that changed (old, new) in the evidence file. Commit `feat(a330-gen): names and positions from the aircraft's own strings`.

### Task 3 [owner]: The naming rule, heard

- [ ] **Step 1:** Generate the ADIRS, Electrical and Captain EFIS panels three ways: the aircraft's titles as written (capitals), in sentence case with its abbreviations kept, and with digits set apart from letters ("ADIRS 1 IR mode"). The owner hears each in the Panels list and rules; record the ruling. The default until then: sentence case, abbreviations as the aircraft writes them, a space before a digit that follows letters, and the side first where two rows of a panel would read alike ("Captain ...", "First officer ...").
- [ ] **Step 2:** Steps 1-5 for the rule in the generator (`test_naming_rule_*`: one test a clause of the ruling); regenerate; the all-aircraft sweeps of plan 01 pass; commit.

### Task 4 [sim]: A live sweep of every control (R10)

**Files:** Create `tools/a330-gen/PanelSweep/**` (`PanelSweep.csproj`, `Program.cs`, `SweepPlanner.cs`, `SweepRunner.cs`, `denylist.txt`, `README.md`); test `tests/MSFSBlindAssist.Tests/A330/A330PanelSweepPlannerTests.cs` (the planner is linked into the test project as the 757's is).

- [ ] **Step 1:** Steps 1-5 for the planner (pure): for each control not denied, the positions to visit in order and the return to the position it started in; a button is planned as one press only where it reports a state. Tests: `A_selector_visits_every_other_position_and_returns`, `A_denied_control_is_skipped_with_its_reason`, `A_guard_is_opened_only_to_reach_its_switch_and_closed_again`, `A_button_with_no_state_is_skipped`.
- [ ] **Step 2:** The runner: through `IniA330Definition`'s own write path, 400 ms apart, read back after 1.5 s (the control's read, and a downstream variable where the curation names one), restore, and write `sweep-<date>.tsv` (key, label, position, sent, read back, downstream, verdict). `--dry-run` prints the plan only.
- [ ] **Step 3:** `denylist.txt`, each line with its reason: the engine masters and start selector, the fire handles, agents and tests, the IDG disconnects, the RAT, the gear lever and gravity extension, the flap and speedbrake levers, the parking brake, the ADIRS selectors (a realignment), the batteries and external power (the cockpit goes dark), the APU, passenger oxygen, the evacuation command, the emergency lights, ditching, cargo and cabin doors, anything that calls the cabin or ground crew.
- [ ] **Step 4 [owner present]:** Run the sweep on the ground, engines off, external power on. Then the denied controls by hand, one at a time, with the owner's go-ahead for each, at least: the parking brake's direction, the ADIRS selectors (measured 2026-11-19: input 1 on each put all three in NAV), the landing and nose light position words, the dome light and wipers. Record every verdict in the evidence file.
- [ ] **Step 5:** Each dead, inverted or mislabeled control fixed in the curation file (a position's `input`, `value` or `proven`; a wrong `read`), one commit a cause: `fix(a330): the <panel> <control>`. Clear the probe side effect the developer doc lists (RMP 1's NAV backup) or say it cannot be cleared from outside.

### Task 5 [sim]: The controls and panels BA does not offer yet (R8, R9)

**Files:** `tools/a330-gen/curation.json`, the regenerated map, `Aircraft/IniA330Definition.Panels.cs` and `Aircraft/A330/A330ControlMap.cs` where a new kind of control needs code; the evidence file.

Work the groups of Task 1's table, each the same way: measure what the control writes and where its state reads (the differential method the generator's header describes: set the event, snapshot the `INI_*` L:vars, record what moved, put it back), then add it to the curation file in the cockpit's own panel with the aircraft's own names, or to `left_out` with the reason.

- [ ] **Step 1:** The first officer's radio panel and audio panel, as separate panels after the captain's (spec D5); the third radio and audio panel.
- [ ] **Step 2:** The standby instrument's keys and the two DCDUs' keys (plans 09's windows press them through `PressFromWindow`: tell plan 09's claimant their keys).
- [ ] **Step 3:** TCAS, the remaining hydraulic and leak-measurement controls, oxygen, emergency and evacuation, gravity gear extension, the cockpit door, the clock's keys, cargo, the maintenance panel, the autobrake buttons (the Landing Gear panel has an autobrake row already: one of the two stays, the other is `left_out`).
- [ ] **Step 4:** Whatever the table still lists. A knob with a turn and a push, or a volume, that today's kinds cannot express: Steps 1-5 for the new kind on in-memory rows (`A330PanelKindsTests`), in `.Panels.cs` and `A330ControlMap.cs`.
- [ ] **Step 5:** Empty and remove `not_yet_offered`; `test_every_catalogued_event_is_a_control_or_left_out_with_a_reason` passes without it. Commit a group at a time: `feat(a330): the <panel> panel`.

### Task 6 [sim]: The order (R11, M6)

**Files:** Create `tools/a330-gen/data/procedure_order.json`; `tools/a330-gen/curation.json` (`panel_order`, `control_order`); the evidence file's "Order" section; tests in `test_generate_a330_map.py`, `test_data_files.py` and `A330ControlMapTests.cs`.

**Interfaces:**
- Produces `procedure_order.json`: `{ "procedures": [ { "id": "COCKPITPREP", "controls": [ "A330_BAT1", "A330_IRS1_MODE", ... ] }, ... ] }`: the procedures in the order a flight meets them, and for each the keys of BA's controls its items act on, in the items' order, each key once. No item text.

- [ ] **Step 1 (M6, with the private reference):** Someone with access captures the simulator's own procedures in order (the owner opens the simulator's checklist window on the A330; the capture and the items' words stay in the private reference), matches each item to the BA control it acts on, and writes `procedure_order.json` here. An item with no control (a check, a call) is left out; a control whose match is unsure is listed in the evidence file by its key and its place, for the owner. `test_procedure_order_holds_only_control_keys` (every entry is a key in `keys.txt`).
- [ ] **Step 2:** The rule, as the 757's: tier 1, what the normal procedures move on every flight, in the order a procedure first uses it, left and right or 1 and 2 together; tier 2, what is set once in preparation, tests and display options; tier 3, non-normal, emergency and maintenance controls, with a guard just before the switch it covers. Panels inside a section: by where the procedures first act on one of the panel's controls. Sections and panels themselves stay the cockpit's. Write the research into the evidence file: for each panel, each control's tier and the procedure and place that put it there.
- [ ] **Step 3:** Steps 1-5: `test_every_control_of_a_panel_is_in_its_control_order_once`, `test_a_guard_sits_just_before_its_switch`, `test_tier_one_follows_the_procedures_order` (reads `procedure_order.json` and the curation's tier marks), and in C# `Every_panels_controls_come_out_in_the_curations_order`; fill `control_order` and `panel_order`; regenerate; commit `feat(a330): panels ordered by daily use`.
- [ ] **Step 4:** Tell plan 06's claimant the order file has landed (the First Officer follows the same order).

### Task 7: The whole map

- [ ] **Step 1:** `python tools/a330-gen/generate_a330_map.py` leaves the committed map unchanged; the generator's tests, the suite and the all-aircraft sweeps pass; `git diff --stat` on `keys.txt` shows additions only.
- [ ] **Step 2:** The developer doc's "Panel layout" rewritten from the curation file (sections, panels, what is left out and why, the naming rule, the order rule), and its "Unverified" lines for controls struck or kept as the sweep found.

## In-sim checks (owner)

1. In the Panels list, walk the Overhead's Electrical and ADIRS panels: the names are the cockpit's, and the first rows are what you touch at the start of every flight.
2. Move five controls you know by ear (beacon, strobe, seat belts, APU master, a fuel pump) from BA: each does what its row says and reads back.
3. The first officer's radio panel: tune a frequency and swap it.

## Handoff to plan 12

- **Pilot-facing:** the panels' names are the cockpit's; controls are ordered by daily use; the new panels (list them). Guide: the A330's panels. Changelog `improvement`.
- **Rule proposals:** [INI-2] reworded for the curation file and the three inputs; INI-14: every catalogued input event is a control or is in `left_out` with a reason, and a name or position name comes from `control_names.json` or carries an override with the aircraft's string and a reason; INI-15: a key never changes or disappears (`keys.txt`).
- **Frozen files:** none expected; a key that had to change, for plans 05, 06, 08, 09.

## Execution record

Kept in this file only, so no other plan's push ever touches it.

- **Status:** not started
- **Claimed by:** none
- **Worked by:**
- **Started:**
- **Landed:**
- **Tests at landing:**
- **Tasks:**
- **Waiting on the owner:**
- **In-sim results:**
- **Changed from the plan:**
- **Rulings made while working:**
