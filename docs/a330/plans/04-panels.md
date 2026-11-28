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

## Measured on 2026-11-22 (in flight, climbing through FL190 to FL230)

- **Predictive windshear is inverted in today's map.** `INI_WX_PWS_SWITCH` and its input event
  `B:AIRLINER_WX_PWS` both read 1 while the E/WD showed the amber memo PRED W/S OFF. After
  `0 (>B:AIRLINER_WX_PWS_Set)` both read 0 and the memo went. So 0 = AUTO and 1 = OFF; the generator's
  `direct("Off", "Auto")` pairs them the other way, and the panel row says "Auto" while the switch is off.
  The 2026-10-16 probe recorded which variable moved; the position names came from the generator's
  conventions, and a read of the switch's own variable agrees with whatever was written, so nothing caught it.
- **A read straight after a write can show the old value.** The same L:var still read 1 in a read sent
  at once after the write and 0 in one a few seconds later (not timed).
- **How many names rest on a convention** (the generator at 7113f8803; the groups overlap): 79 selectors
  named through `direct()` (position names in an assumed order), 23 push-buttons through `pb()` (the
  "pushed in reads 1" convention), and 43 controls whose read is their own input event. Predictive
  windshear is one of the 79; packs 1 and 2 and hot air 1 and 2, which toggle (plan 06's block), are
  among the 23.
- Read in flight the same day, not a proof: `INI_TCAS_STBY_STATE` 1, `INI_TCAS_MODE_PEDESTAL` 2,
  `TRANSPONDER STATE:1` 4, `INI_WX_SYS_SWITCH` 0. These are what the First Officer's own writes would
  leave, so they cannot settle the transponder and TCAS question below by themselves. The captain's ND,
  read complete by the reader program at the same time, showed neither TA ONLY nor TCAS STBY, and the
  E/WD showed no TCAS memo: that agrees with today's names (TCAS at TA/RA) and would not if the two were
  crossed (TCAS would then be at TA ONLY), but only once this aircraft's ND has been seen drawing TA ONLY
  with TCAS at TA. A lead for Task 4, not a verdict.

## Found by a code review on 2026-11-22 (read in code, not run in the sim)

Each is a task below; each claim is checked against the code before its fix.

- **A refused write is reported as done.** `HandlePanelSet` sets `handled = true` on every branch, also
  when `CanCalc` refuses and when `SetInputEvent` returns false (`IniA330Definition.Panels.cs`, the
  switch on `c.Mode`), so MainForm keeps the pilot's pick in the combo and never reads the control back;
  `PressFromWindow` returns true for it too. Task 4a.
- **A pick is never read back.** A write the aircraft ignores (a closed guard, no power, a toggle) leaves
  the row showing the wrong position, silently. The 757's set path reads first, writes only when it
  differs, reads back and corrects its row (`BBS757Definition.Interaction.cs`). Task 4a.
- **The toggle mode presses on an unknown state** ("an unknown state counts as differing, so a cold cache
  still actuates"): right for the engine masters, which take their position by value, wrong for a true
  toggle such as the packs. Task 4a.
- **Momentary keys say nothing and show nothing.** `GetButtonStateMapping` returns nothing and the map's
  buttons carry no state variable, though the ECAM page keys (`INI_ECAM_ACTIVE_PAGE`), the radio panel's
  keys (`INI_SELECTED_RADIO_CPT`), APU START, the agents and the IDG have a readable result. Task 4a.
- **Guards are separate combos.** 27 guards (`guard()` in the generator) have no link to the switch they
  cover, and the curation schema has no field for one, so the planner's guard test has nothing to plan
  from. Whether the aircraft refuses a guarded switch with its guard closed is unmeasured. Tasks 2 and 4.
- **Sliders speak 0 to 100 %** whatever the control's range: tilt runs 0 to 30, so the row's "15 = level"
  is 50 on the slider; the temperature and landing elevation sliders say no °C or feet (the A32NX says
  both); the map's `step` is unused. Task 4a.
- **Names that look wrong** (aviation logic or the variables' own names; for the sweep to settle): the
  transponder and TCAS selectors may be crossed (the event `TCAS_MODE` is labelled "Transponder mode" and
  reads `INI_TCAS_STBY_STATE`; `TCAS_STBY` is labelled "TCAS mode"), and the First Officer's Line-up
  "TCAS: TA/RA" writes 2 to the second; the EFIS DMC selector (Normal at 0, a captain's event offering
  "First officer on 3"); the clock's elapsed-time Reset (spring-loaded on Airbus clocks); the fuel pumps
  (left pump 1 reads OUTER, right pump 1 reads INNER); the cabin pressure MODE SEL (its read stays 0); the
  radio panel's selected radio (no entry for AM or the NAV backup keys); the altimeter unit (read is its
  own event, and the First Officer speaks inHg or hPa from it). Task 4 Step 4.
- **Controls no input-event count can find:** what the A32NX and Headwind A330 panels drive through stock
  events (transponder code entry, COM tuning on the radio panel, rudder trim and its reset), and by
  aviation logic EMER ELEC PWR, the cabin pressure manual V/S, FAC 1 and 2, GPWS LDG FLAP 3, the first
  officer's master warning and caution, rain repellent, the lighting knobs, the CVR test. Tasks 1 and 6.
- **Stock COM tuning is unmeasured here:** `StockComTuningRefusal` is null, so the surroundings window
  tunes the A330's COM 1 with stock events; it reads the result back, so a dead write is heard. Task 5.

## Review Focus

- An input event in the catalogue that is neither a control nor left out: the generator fails, naming it (Task 2 test).
- A position name paired with the wrong value (the aircraft counts some selectors from the top, some from 1): each pairing is marked proven or unproven, and an unproven one is listed for the owner, never shipped as fact (Task 2, Task 4).
- Two rows of one panel with one name after the aircraft's titles are applied (the captain's and first officer's copies): the side comes first in the name, and the all-aircraft sweep of plan 01 still passes (Task 3 test).
- A regenerated map that drops or renames a key silently: a test compares the key set with the committed list of keys and fails on a removal (Task 2 test).
- A control whose read is its own input event and that reads back what was written even when the aircraft ignored it ([DBG-5]): the sweep checks a downstream variable where one exists and marks the rest "read is its own event" (Task 4).
- A swapped pair of position names that reads back exactly as written (predictive windshear, measured 2026-11-22): a read of the control's own variable never proves a name. A pairing is proven only by an indication the control does not write itself, its proof: an E/WD memo, a lower ECAM page, a lamp, or a simulator variable the aircraft drives (Task 4 Step 0). A pairing with no proof stays unproven and is listed for the owner.
- A verdict taken before the aircraft has caught up (the same switch's L:var lagged its write): the runner waits for the read and the proof to settle, up to a limit, and records the time it took, instead of one read at a fixed delay (Task 4 Step 2).

---

### Task 0 [controller]: Claim this plan

As plan 01's Task 0, with this file's path and `docs(a330): claim plan 04` (`Plan: 04`). Branch from the base commit the index records for early plans.

### Task 1 [sim]: The catalogue and the aircraft's own names (M5)

**Files:** Create `tools/a330-gen/data/input_events.tsv`, `tools/a330-gen/data/control_names.json`, `tools/a330-gen/test_data_files.py`, `docs/a330/evidence/04-panels.md`.

**Interfaces:**
- Produces `control_names.json`: `{ "source": { "date": "...", "simulator": "..." }, "controls": { "ADIRS1_MODE": { "title": "ADIRS1 IR MODE", "positions": { "0": "OFF", "1": "NAV", "2": "ATT" } } } }`. A control's key is its input event's name without `AIRLINER_`; a position's key is the aircraft's own (a number, or `ON` / `OFF`).

- [ ] **Step 1:** The names file: someone with access runs the private reference's export and commits its output here unchanged. Steps 1-5 for its shape: `test_control_names_holds_only_titles_and_position_names` (no other key; every value a string of at most 60 characters), `test_every_control_has_a_title`; commit `feat(a330-gen): the aircraft's own control names`.
- [ ] **Step 2:** The simulator's list: with the A330 loaded, start a Debug build of MSFSBA once (the owner's go-ahead: it opens the app), select the A330, and copy the A330's block from `%APPDATA%\MSFSBlindAssist\logs\input_events.txt` to `tools/a330-gen/data/input_events.tsv` (name, tab, hash; a first line with the date and the simulator's version). Commit it.
- [ ] **Step 3:** In the evidence file, one table: every catalogued event, the aircraft's title for it (or "no name"), and whether today's map has it. Then the same by group, with counts. Then the A32NX's and the Headwind A330's panel rows with no A330 match, by panel (a control the aircraft drives through stock events never shows in an input-event count: transponder code, COM tuning, rudder trim). This is the list Task 5 works through.

### Task 2: The generator on data, with nothing unaccounted for

**Files:** Create `tools/a330-gen/curation.json`, `tools/a330-gen/keys.txt`, `tools/a330-gen/test_generate_a330_map.py`; modify `tools/a330-gen/generate_a330_map.py`, `tools/a330-gen/README.md`; `Resources/a330_control_map.json` (regenerated).

**Interfaces:**
- Produces `curation.json`:
  ```json
  {
    "controls": { "<EVENT or own key>": { "key": "A330_IRS1_MODE", "section": "Overhead", "panel": "ADIRS", "kind": "select",
                  "mode": "set", "read": "INI_IRS1_STATE",
                  "positions": [ { "value": 0, "input": 0, "name": "0", "proven": true }, ... ],
                  "proof": { "kind": "ewd_memo", "text": "PRED W/S OFF", "when": "1" },
                  "guard": "A330_ENG1_FIRE_COVER", "state": "INI_ECAM_ACTIVE_PAGE",
                  "unit": { "name": "degrees", "min": -15, "max": 15 },
                  "lamps": [ ... ], "holdMs": 150, "pressesToCycle": 3 } },
    "name_overrides": { "A330_IR1": { "label": "...", "aircraft_says": "IR1", "why": "..." } },
    "position_overrides": { "A330_IR1": { "1": { "label": "On", "aircraft_says": "PRESS", "why": "the aircraft's string names the action" } } },
    "left_out": { "CPT_MCDU_A": "pressed in the MCDU window through its own key flags", ... },
    "panel_order": { "Overhead": ["Electrical", ...] },
    "control_order": { "ADIRS": ["A330_IRS1_MODE", ...] }
  }
  ```
  `"name": "0"` is the key of the aircraft's string for that position; the generator writes the string's text as the label. `keys.txt` is every key the map has ever had, one a line. `proof` names the indication that proves the control's positions (Task 4 Step 0): `kind` is `ewd_memo`, `sd_page` (with `page` and the text the page shows), `lamp` (its variable), or `simvar`; `when` is the position at which the indication shows. `proven` turns true only from a sweep verdict that saw the proof follow the position's name. `guard` names the cover over a switch (the guard's own row stays, just before it); `state` names the variable a button's press changes, or is absent; `unit` gives a continuous control's spoken unit and the aircraft's own range; `pressesToCycle` is how many presses bring a multi-press key (the chronometer) back to where it began.

- [ ] **Step 1:** Move what `generate_a330_map.py` holds as Python calls into `curation.json` (a one-off script, not committed), keeping today's labels as overrides for now, so the regenerated map is byte-identical to the committed one. Tests: `test_the_map_regenerates_identically_from_curation`, `test_every_catalogued_event_is_a_control_or_left_out_with_a_reason`, `test_a_left_out_event_that_is_also_a_control_is_refused`, `test_no_key_ever_disappears` (against `keys.txt`), `test_a_control_naming_an_event_the_catalogue_lacks_is_refused`. The second test fails now: fill `left_out` only for the events the definition presses itself (the FCU, the brakes, the baro knobs) and the MCDU keys, and put every other event in a `not_yet_offered` list, which the test accepts only while this plan is open (Task 5 empties and removes it). Commit `refactor(a330-gen): the map from a curation file; every event accounted for`.
- [ ] **Step 2:** Names from the aircraft: the generator takes each control's label from its title in `control_names.json` and each position's label from its position name there, applying the naming rule Task 3 sets, unless an override says otherwise. Tests: `test_a_label_is_the_aircrafts_title`, `test_an_override_without_the_aircrafts_string_and_a_reason_is_refused`, `test_an_action_word_is_never_a_position_name` ("PRESS", "PUSH", "PULL", "TURN" need an override), `test_an_unproven_pairing_is_listed`. Regenerate; list every label that changed (old, new) in the evidence file. Commit `feat(a330-gen): names and positions from the aircraft's own strings`.

### Task 2a: Predictive windshear, measured inverted (no sim)

The one inversion already measured (2026-11-22, this plan's block). It needs no sim and may run before Task 1; whichever of plans 04 and 06 is claimed first does it, together with plan 06's Task 1a in the same commit (the First Officer's values are pinned to this map's labels, so one without the other fails the suite), and tells the other plan's claimant. The key `A330_WX_PWS` does not change.

- [ ] **Step 1:** Steps 1-5 with `test_predictive_windshear_reads_0_as_auto` (Python) and, in `A330ControlMapTests`, `Predictive_windshear_position_0_is_auto`: the generator's line becomes `select("WX_PWS", P, WX, "Predictive windshear", "WX_PWS", "INI_WX_PWS_SWITCH", [(0, 0, "Auto"), (1, 1, "Off")])`, its comment citing this plan's 2026-11-22 block; regenerate. Commit `fix(a330): the Weather Radar predictive windshear` with plan 06's Task 1a.
- [ ] **Step 2:** In the curation file (Task 2), this control carries `"proof": { "kind": "ewd_memo", "text": "PRED W/S OFF", "when": "1" }` and both positions `proven`, with the measurement's date.

### Task 3 [owner]: The naming rule, heard

- [ ] **Step 1:** Generate the ADIRS, Electrical and Captain EFIS panels three ways: the aircraft's titles as written (capitals), in sentence case with its abbreviations kept, and with digits set apart from letters ("ADIRS 1 IR mode"). The owner hears each in the Panels list and rules; record the ruling. The default until then: sentence case, abbreviations as the aircraft writes them, a space before a digit that follows letters, and the side first where two rows of a panel would read alike ("Captain ...", "First officer ...").
- [ ] **Step 2:** Steps 1-5 for the rule in the generator (`test_naming_rule_*`: one test a clause of the ruling); regenerate; the all-aircraft sweeps of plan 01 pass; commit.

### Task 4 [sim]: A live sweep of every control (R10)

**Files:** Create `tools/a330-gen/PanelSweep/**` (`PanelSweep.csproj`, `Program.cs`, `SweepPlanner.cs`, `SweepRunner.cs`, `denylist.txt`, `README.md`); test `tests/MSFSBlindAssist.Tests/A330/A330PanelSweepPlannerTests.cs` (the planner is linked into the test project as the 757's is).

**Prerequisite:** plan 02's display reader client has landed (`IA330Displays`), so the runner can read the E/WD and the lower ECAM page; plan 08's memo and page lists give the proofs' words.

- [ ] **Step 0 (no sim): the proof table.** In the evidence file, for every control: the indication that proves its positions (the review focus's "proof"), from the A330's E/WD memo list and lower ECAM pages, for example predictive windshear → PRED W/S OFF memo; seat belts and no smoking → their memos; strobe → STROBE LT OFF; packs, bleeds and crossbleed → the BLEED page; fuel pumps and crossfeed → the FUEL page; hydraulic pumps → the HYD page; generators, batteries and bus ties → the ELEC pages; probe heat and anti-ice → their memos; TCAS and transponder → the ND or the memo; lights with no memo → the simulator's light state (`LIGHT STATES`, `LIGHT LANDING:n` and the like). A control with no proof is listed for the owner as "read is its own variable". Write each proof into the curation file.
- [ ] **Step 1:** Steps 1-5 for the planner (pure): for each control not denied, the positions to visit in order and the return to the position it started in; a button is planned as one press only where it reports a state; the lower ECAM page a control's proof needs is selected before it. Tests: `A_selector_visits_every_other_position_and_returns`, `A_denied_control_is_skipped_with_its_reason`, `A_guard_is_opened_only_to_reach_its_switch_and_closed_again`, `A_button_with_no_state_is_skipped`, `A_proof_on_a_lower_ecam_page_selects_the_page_first`.
- [ ] **Step 2:** The runner: through `IniA330Definition`'s own write path, 400 ms apart; after each write it reads the control's read and its proof until both settle or 5 s pass, and records the time each took; restore, and write `sweep-<date>.tsv` (key, label, position, sent, read back, settle ms, proof before, proof after, verdict). Verdicts: `proven` (the proof followed the position's name), `inverted` (the proof followed the other name), `read-only` (the read moved, there is no proof), `dead` (nothing moved), `lagged` (settled only after 1.5 s: still a pass, its time goes to plan 06), `toggle` (the same position written twice flips the read back), `not sent` (the write was refused before it left: `CalcPathVerified` false, or a refused input event, never called dead). `--dry-run` prints the plan only.
- [ ] **Step 2b, the runner's other duties** (each with a planner or runner test): before the first write, a journal of every control's starting value, and `--restore <journal>` after a crash or Ctrl+C; restore by replaying the event's own starting value, never through the pairing being tested (an inverted pairing would "restore" to the wrong side), read it back and stop the sweep if it did not hold; each position written twice, to find toggles; a snapshot of the `INI_*` variables before and after each write (Task 5's differential method), so a side effect (RMP 1's NAV backup) is recorded and a variable that follows the control becomes a candidate proof; the electrical, bleed and hydraulic state recorded with each verdict, and a second pass with the APU bleed on for the packs, bleeds and crossbleed; each continuous control at its minimum, middle and maximum against the aircraft's own value where it has one (`INI_LANDING_ELEVATION`, `INI_WXR_GAIN`); a multi-press key restored by its `pressesToCycle`.
- [ ] **Step 3:** `denylist.txt`, each line with its reason: the engine masters and start selector, the fire handles, agents and tests, the IDG disconnects, the RAT, the gear lever and gravity extension, the flap and speedbrake levers, the parking brake, the ADIRS selectors (a realignment), the batteries and external power (the cockpit goes dark), the APU, passenger oxygen, the evacuation command, the emergency lights, ditching, cargo and cabin doors, anything that calls the cabin or ground crew.
- [ ] **Step 4 [owner present]:** Run the sweep on the ground, engines off, external power on. Then the denied controls by hand, one at a time, with the owner's go-ahead for each, at least: the parking brake's direction, the ADIRS selectors (measured 2026-11-19: input 1 on each put all three in NAV), the landing and nose light position words, the dome light and wipers; every guarded control with its guard closed (does the aircraft refuse it? if not, Task 4a's refusal); and each name the review found doubtful, by its proof: the transponder and TCAS selectors (the ND's TA ONLY, the E/WD's TCAS STBY memo, `TRANSPONDER STATE:1`), the weather radar system selector (the ND's radar picture), the EFIS DMC selector, the clock's elapsed-time reset, the fuel pumps (the FUEL page), the cabin pressure MODE SEL (the PRESS page), the radio panel's selected radio, the altimeter unit (the PFD's baro unit). Record every verdict in the evidence file. A proof the ground does not show (a memo only shown in flight) goes to plan 11's flight by its key, and stays unproven until then.
- [ ] **Step 5:** Each dead, inverted or mislabeled control fixed in the curation file (a position's `input`, `value` or `proven`; a wrong `read`), one commit a cause: `fix(a330): the <panel> <control>`. A fix that changes what a position's value means is agreed with plan 06's claimant first: the First Officer writes these values (`IniAirbusKeys`). Clear the probe side effect the developer doc lists (RMP 1's NAV backup) or say it cannot be cleared from outside.
- [ ] **Step 6:** Steps 1-5 for the gate: `test_every_offered_position_is_proven_or_listed` (a position with `proven` false is in the evidence file's unproven list, by key and position) and, in C#, `An_unproven_position_is_marked_in_the_map`, so plan 06 can refuse to write it. Commit.

### Task 4a: What a panel row does with a pick (the review's code findings)

**Files:** `Aircraft/IniA330Definition.Panels.cs`, `Aircraft/A330/A330ControlMap.cs`; tests `A330PanelSetTests.cs`, `A330PanelButtonTests.cs`, `A330PanelSliderTests.cs`. `Aircraft/IniA330Definition.Handlers.cs` is frozen after plan 01: `PressFromWindow`'s false for a refused write is this plan's one named exception there, listed in the handoff. Each fix is checked against the code first; the announcement convention of the index applies to every sentence.

- [ ] **Steps 1-5, a pick is read back:** a selector pick reads the control first and writes only when it differs; after the control's settle time (Task 4's) it reads back and corrects its row: silent when the pick landed, the real position said once when not ([CORE-7]); `HandlePanelSet` returns not handled for a refused write, and `PressFromWindow` false. The 757's set path is the model. Tests `A_refused_write_is_not_handled`, `A_pick_the_aircraft_ignored_resyncs_its_row_and_says_where_it_is`, `A_pick_that_landed_says_nothing`. Commit.
- [ ] **Steps 1-5, a true toggle never presses blind:** a `toggle` control with no by-value code refuses aloud while its read is unknown ([FO-18]'s rule: unread is unknown, never "differs"); the engine masters, which take their position by value, keep pressing. Tests `A_toggle_with_no_read_is_not_pressed`, `An_engine_master_with_no_read_is_still_set_by_value`. Commit.
- [ ] **Steps 1-5, a key's press is confirmed once:** a button with a `state` reads it back once after the press and says the new state, as the FlyByWire A32NX's and the A380's button read-back do (`GetButtonStateMapping`); a button with none says "<name> pressed", as the Fenix's press buttons with no light do. Tests `An_ECAM_page_key_reads_back_its_page`, `A_stateless_key_says_pressed`, `A_key_with_a_state_does_not_also_say_pressed`. Commit.
- [ ] **Steps 1-5, a guarded switch:** where Task 4 found that the aircraft moves a guarded switch with its guard closed, BA refuses the write aloud while the guard reads closed ("<name>: guard closed"). Test `A_guarded_switch_is_refused_while_its_guard_is_closed`. Commit.
- [ ] **Steps 1-5, sliders in the aircraft's units:** a continuous control with a `unit` is offered and spoken in it (degrees of tilt, °C, feet, as the A32NX speaks its temperature and landing elevation), in steps of its `step`; the label's own numbers agree with the slider ("Tilt 0 = level"). Tests `Tilt_is_said_in_degrees`, `A_slider_moves_by_its_step`, `A_temperature_is_said_in_degrees_C_under_de_DE`. Commit.
- [ ] **Steps 1-5, the panels' announcements sound like the other Airbus aircraft's** (the index's convention; the announcements review of 2026-11-22): a lamp and a status row say "On" and "Off", never "Lit" (the A330 says "<label> <legend> light: Lit", the Fenix "<name>: On"); the parking brake "On" and "Off" (the A32NX's "Parking Brake: On"); the autobrake LO, MED and MAX, the cockpit's words, which the A32NX says too (the A330 says "Low", "Medium"); the gear "Down" and "Up". A control's own name stays the aircraft's (Task 3). The master warning and master caution keys read back their state (`INI_MASTER_WARNING_ACTIVE`, `INI_MASTER_CAUTION_ACTIVE`, by name: measure them) once after a press, as the key step above does. Tests `A_lamp_says_on_not_lit`, `The_parking_brake_says_on`, `The_autobrake_says_the_cockpits_words`, `Master_warning_reads_back_after_its_press`. Commit.
- [ ] **Steps 1-5, the speedbrake lever is said:** today it is silent with no Ctrl+M row (`A330_SPDBRK`; plan 01 took the row away because it muted nothing). It is said as the A32NX says it ("Spoilers retracted", "Spoilers 30 percent", "Spoilers full", in its 10 % bands) from `ProcessPanelUpdate`, the first reading silent, through the mute wrap, and its Ctrl+M row "Speedbrake lever" comes back because it now mutes something. Tests `A_band_change_is_said_once`, `The_first_reading_is_silent`, `A_muted_row_says_nothing`. Commit.
- [ ] **Step 6 [sim], lamp values:** measure what the lamps read (Task 4's sweep): a lamp whose variable reads other than 0 and 1 (a dimmed or flashing lamp) would be said as a number today, because the spoken path compares the raw value; such a lamp gets a value rule so it says "On", "Off" or "Flashing". Test on the measured values.

### Task 5 [sim]: The controls and panels BA does not offer yet (R8, R9)

**Files:** `tools/a330-gen/curation.json`, the regenerated map, `Aircraft/IniA330Definition.Panels.cs` and `Aircraft/A330/A330ControlMap.cs` where a new kind of control needs code; the evidence file.

Work the groups of Task 1's table, each the same way: measure what the control writes and where its state reads (the differential method the generator's header describes: set the event, snapshot the `INI_*` L:vars, record what moved, put it back), then add it to the curation file in the cockpit's own panel with the aircraft's own names, or to `left_out` with the reason.

- [ ] **Step 1:** The first officer's radio panel and audio panel, as separate panels after the captain's (spec D5); the third radio and audio panel. COM tuning and the transponder code as the A32NX's panels offer them; measure the surroundings window's stock COM tuning on this aircraft and set `StockComTuningRefusal` if the aircraft ignores it ([A380C-18]).
- [ ] **Step 2:** The standby instrument's keys and the two DCDUs' keys (plans 09's windows press them through `PressFromWindow`: tell plan 09's claimant their keys).
- [ ] **Step 3:** TCAS, the remaining hydraulic and leak-measurement controls, oxygen, emergency and evacuation, gravity gear extension, the cockpit door, the clock's keys, cargo, the maintenance panel, the autobrake buttons (the Landing Gear panel has an autobrake row already: one of the two stays, the other is `left_out`).
- [ ] **Step 4:** Whatever the table still lists. A knob with a turn and a push, or a volume, that today's kinds cannot express: Steps 1-5 for the new kind on in-memory rows (`A330PanelKindsTests`), in `.Panels.cs` and `A330ControlMap.cs`.
- [ ] **Step 5:** Empty and remove `not_yet_offered`; `test_every_catalogued_event_is_a_control_or_left_out_with_a_reason` passes without it. Commit a group at a time: `feat(a330): the <panel> panel`.

### Task 6 [sim]: The order (R11, M6)

**Files:** Create `tools/a330-gen/data/procedure_order.json`; `tools/a330-gen/curation.json` (`panel_order`, `control_order`); the evidence file's "Order" section; tests in `test_generate_a330_map.py`, `test_data_files.py` and `A330ControlMapTests.cs`.

**Interfaces:**
- Produces `procedure_order.json`: `{ "procedures": [ { "id": "COCKPITPREP", "controls": [ "A330_BAT1", "A330_IRS1_MODE", ... ] }, ... ] }`: the procedures in the order a flight meets them, and for each the keys of BA's controls its items act on, in the items' order, each key once. No item text.

- [ ] **Step 1 (M6, with the private reference):** Someone with access captures the simulator's own procedures in order (the owner opens the simulator's checklist window on the A330; the capture and the items' words stay in the private reference), matches each item to the BA control it acts on, and writes `procedure_order.json` here. An item with no control (a check, a call) is left out; an item naming a control BA does not offer yet (a squawk, the rudder trim) is listed as "missing control" for Task 5, never dropped as "no control"; a control whose match is unsure is listed in the evidence file by its key and its place, for the owner. `test_procedure_order_holds_only_control_keys` (every entry is a key in `keys.txt`).
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
4. In the Weather Radar panel, set Predictive windshear to Off, then Auto: the E/WD's PRED W/S OFF memo shows at Off and goes at Auto (Task 2a).

## Handoff to plan 12

- **Pilot-facing:** the panels' names are the cockpit's; controls are ordered by daily use; the new panels (list them). Guide: the A330's panels. Changelog `improvement`.
- **Rule proposals:** [INI-2] reworded for the curation file and the three inputs; INI-14: every catalogued input event is a control or is in `left_out` with a reason, and a name or position name comes from `control_names.json` or carries an override with the aircraft's string and a reason; INI-15: a key never changes or disappears (`keys.txt`).
- **Frozen files:** `Aircraft/IniA330Definition.Handlers.cs`, by the named exception of Task 4a (`PressFromWindow` returns false for a refused write); a key that had to change, for plans 05, 06, 08, 09.
- **As landed so far (2026-11-27):** no key changed or disappeared (`keys.txt`: 281, additions only). Pilot-facing changes so far: a pick is read back and a refused write is never reported as done, and a quick pick back reaches the aircraft too; 73 push-buttons are pressed with one input-event set, only when their read is known and differs (they toggle); a key's press is confirmed once by its state or "<name> pressed"; weather radar tilt is a combo in degrees; lamps, status rows and the parking brake say On and Off, the autobrake LO, MED and MAX; the speedbrake lever is said in the A32NX's bands, with a Ctrl+M row "Speedbrake lever". Names and order are not yet the aircraft's (Tasks 1, 3, 6 wait). Guide: the A330 panels chapter (what a pick and a press say). Changelog: `improvement`.
- **Rule proposals from this work:**
  - A new INI rule (number at plan 12) for the measured toggles: "A push-button the aircraft toggles (`A330Write.PressedByValue`: `mode: toggle` with `press`) is pressed by ONE `SetInputEvent` of its press value and nothing after it (the model the 2026-11-27 sweep fits: every set to its event is a press; measured, a set of 1, a hold and a set of 0 left the switch where it was), only when its READ is known and differs. Its positions' inputs are never written, and it is never judged or restored by its input event's own value, which says nothing about the switch." [INI-1]'s "push-button 0 = ON" then points to it, with its mirrors and `docs/ini-a330.md`'s convention line changed together.
  - CORE-7's "Today's cases" gains the A330's: its key state read-back (a key with a state row says that row's description once after the settle time), its "<name> pressed" for a key with no state, the display window press's position sentence ("<label>: <position>" when a window's pick did not land), and the speedbrake lever's quiet while it travels to the pilot's own pick.
  - CI (`.github/workflows/tests.yml`, plan 12's file): one step that runs `python -I -m unittest discover -s tools/a330-gen -p "test_*.py"`. Two of those guards (the keys and the 73 toggles' press) are also C# tests now (`A330ControlMapTests`), but the rest run nowhere in CI.
  - CORE-4's count of standalone `tools/*` projects becomes 18 (`tools/a330-gen/PanelSweep` is new); `docs/development.md` lists it.
- **A commit never to run:** 1790229dc (the curation commit of Task 4 Step 5) routes the 73 through the calculator `_Push` and `_Release`, until a5219c55f routed them by input event. Do not build or bisect onto it for a live check.
- **Requests for frozen or other plans' files:** plan 06's `Aircraft/IniA330Definition.FirstOfficer.cs` was changed by one named-exception branch (`FoWriteRoute`: a pushed toggle routes by its input event, `A330Write.PressedByValue`), because plan 06 was not claimed. For plan 06: 19 First Officer keys now refuse to press while their read is unknown (they were blind sets that toggle), and each press is one input-event set (no hold, no release). Within a pick's 1.5 s read-back the panel plans from that pick, not the cache: the same position written again sends nothing, and the other position presses again. Plan 01's `tests/MSFSBlindAssist.Tests/A330/IniA330DefinitionTests.cs`: `The_speedbrake_lever_cache_has_no_ctrl_m_row` inverted to `The_speedbrake_lever_has_its_ctrl_m_row` (the row came back, Task 4a). For MainForm (frozen): a slider-unit hook would let a continuous control stay a slider and speak its unit (today a control with a unit is a combo of its steps). Also seen on the A32NX: its speedbrake bands speak the lever's travel after the pilot's own pick.

## Execution record

Kept in this file only, so no other plan's push ever touches it.

- **Status:** waiting on owner (the code tasks that needed no names or order are done and reviewed; Tasks 1 Step 1, 3, 4 Steps 0 and 4's by-hand part, 5, 6 and the in-sim checks wait, below). Pushed 2026-11-28 to the shared branch at the owner's word (9bba156f9..ba0613c65, a fast-forward, the claim included). The private push check could not run on this machine; the diff and the commit messages were re-read against spec section 4 instead (no location of the private reference, nothing on how the display reader works, no local paths).
- **Claimed by:** wolfman1360
- **Worked by:** wolfman1360, with Claude (subagent-driven development: an implementer and a review a task, a whole-plan review)
- **Started:** 2026-11-27, from 9bba156f9 (the branch tip, plans 01 to 03 landed)
- **Landed:**
- **Tests at landing:** 26,327 passed, 0 failed (filter `FullyQualifiedName!~ClaudeRulesHookTests`; 26,190 at the start), on e1e8c5608 after the whole-plan review's fix wave; the generator's 31 Python tests pass (none at the start). Whole-plan review (opus): "with fixes", no Critical; five Important and the minors fixed in one wave (d025af3d3 to e1e8c5608), its re-review clean.
- **Tasks:**
  - 0 claim 4095bab2b.
  - 2a predictive windshear with plan 06's Task 1a 302fdad9d; its Step 2 (the proof in the curation) 8c9ce1de9.
  - 1 Step 2, the catalogue (649 events), 09c3770d1; Step 3, the catalogue table, 726c8b3b3 and ec9035d99. Step 1 (the names file) waits.
  - 2 Step 1, the curation file, f0d9b4d33, with the rest of every event accounted for in 726c8b3b3 and ec9035d99 (271 controls, 32 left out, 346 not offered yet). Step 2 waits on the names file.
  - 2b (controller's, from Task 2's Interfaces and Task 4 Step 6): the map carries guard, state, unit, proof and proven, and the unproven list `tools/a330-gen/unproven.txt`, 37aa939ed and 944a36426. Task 4 Step 6's gate is its tests.
  - 4 Step 1 planner 8ea3ee810; Step 3 deny list 41adc81a1, 1284d272c; Steps 2 and 2b the runner 86a9df963, 0f90640db, fixes ab7afa452 to 00b59045d; Step 4 the sweep, run once (In-sim results); Step 5 for its first cause (the push-buttons toggle) 1790229dc, a5219c55f, 799710f21, 8785aed8e.
  - 4a: a pick is read back ce6dbaa57, b474af667; a toggle never presses blind 1afe87380; a key's press confirmed once cb27f8886, 5accf342f, 7591cabfc, 7123a8059; sliders in units 6263983f4; the announcements c9ed0d8e4; the speedbrake 37348fa6d, fe9716fbe. The guarded-switch step and Step 6 wait.
  - 7 Step 1 checked and Step 2 written for what is true now, fdc0686a3.
  - The whole-plan review's fix wave: the push-buttons pressed by ONE input-event set (the two-set press measured dead at 23:05) d025af3d3, c2974a733; no real-time test waits 62b9d0a84; a pick and a quick pick back both reach the aircraft a5f754eff; the key and toggle guards in the C# suite 4b89d7c5a; no label leaned on, a missing catalogue an error edabae0e8; the families never press blind 6d15765fa; the snapshot block filled to its size (a marshaling crash seen once) b55f5d6fa; docs and handoff e1e8c5608.
- **Waiting on the owner:**
  - Confirm pack 2 and engine 1 bleed are on (they started on; the 23:54 restore pressed each once and the tool read them on, but the simulator-link MCP still read them off; it has been wrong before).
  - One pick each way from the app's own panels on a few of the 73 push-buttons (pack 1, a fuel pump, a flight director), and the First Officer's cockpit preparation, which switches several of them.
  - Task 1 Step 1 and Task 6 Step 1: the names file and the procedure order from the private reference (not on this machine).
  - Task 3: the naming rule, heard.
  - Task 4 Step 0 (the proofs, from plan 08's lists), Step 4's by-hand part (the denied controls one at a time, the guards with their guards closed, the doubtful names by their proofs), the APU-bleed pass, and Step 5 for the other causes (the evidence file's waiting list).
  - Task 4a's guarded-switch step and Step 6 (lamp values), Task 5, Task 6, and the plan's in-sim checks.
  - Questions: deny FADEC ground power, the generators and crew oxygen too? Tilt 0 said "0 degrees" (neither the A32NX nor the Fenix says "Level"): keep? The autobrake's rest word stays "Off" (the A32NX says "DIS"): keep?
- **In-sim results:** 2026-11-27, MSFS 2024 1.9.24.0, A330-200 VIP (GE), on the ground, engines off, external power on (the batteries and external power A switched on by Claude for it). The catalogue: 649 input events, all 271 map events among them. The sweep (`tools/a330-gen/PanelSweep`, the app's own write route), 151 units, about 13 minutes, no SimConnect exception: read-only 172, toggle 73, dead 10, already there 2, none lagged. The 73 push-buttons toggle (the evidence file has the measured rule); the dead are ADR 1 to 3, pressurization MODE SEL, master warning and caution, and four radio panel keys. Every position except predictive windshear stays unproven. Measurement traps found: the simconnect MCP's read of an L:var can return 0 (or a stale value) when it is wrong, and its calculator `_Set` route toggles a push-button on every write; by the 23:05 trial so does the app's input-event route. 23:05: the two-set press (set 1, 150 ms, set 0) was dead on pack 1 and the captain's flight director (both sets are presses); one earlier attempt crashed inside SimConnect's message marshaling before any write (fixed in b55f5d6fa, cause inferred). 23:54, after the single-set fix: one set flipped each and a second flipped it back, both ended at their start; the restore then put back all 165 controls of the 21:29 journal, pressing pack 2 and engine 1 bleed once each.
- **Changed from the plan:**
  - The sweep planner and runner core live in the app (`Aircraft/A330/Sweep/`), xUnit-tested; the tool links them (the 757's planner is not in the test project, and the test project is frozen).
  - The unproven list is a generated file under `tools/a330-gen`, not the evidence file (no test may read `docs/a330/`).
  - A refused write stays handled, so MainForm never writes the read L:var raw; `PressFromWindow` returns false for it.
  - A continuous control with a unit is a combo of its steps (MainForm's slider always speaks 0 to 100); only tilt has a unit until the others' ranges are measured.
  - The key state rows (ECAM page, selected radio, master warning and caution, APU START) are announced by the definition with its own baseline and a 5 s quiet start, so a press is heard once and a change back is still spoken.
  - The sweep's eighth verdict `mislabeled`, its stricter settle rule, the families of keys sharing one state, and the restore of a toggle by its read.
  - The catalogue was captured with the worktree's own Debug build (the `navdatareader` folder copied into its output from the installed copy, for the runtime check).
  - Plan 01's speedbrake Ctrl+M test inverted; one branch in plan 06's `FoWriteRoute` (Handoff).
- **Rulings made while working** (Claude's; the owner may reverse any): the worktree from the branch tip; nothing pushed without the owner's word; the catalogue test skips while the catalogue is absent; the planner in the app; the unproven list generated; a refused write stays handled; the read-back waits 1.5 s until settle times are measured; a unit only where the scale is known; the runner after Task 4a's write planner; packs and hot air not re-marked as toggles until the sweep said so (it did, with the other 71); the sweep's family rule; Ctrl+C never splits a press from its release; `--restore` stops at a push-button that did not come back; the FCU's events offered by its own flags are left out; the evidence file's tables kept small for a screen reader; the FirstOfficer branch as a named exception.
