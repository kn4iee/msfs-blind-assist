# A330 Read-outs and App-wide Parity Implementation Plan (plan 10, later)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** Every key that reads or opens something on the app's other Airbus aircraft does so on the A330 or is recorded as not applicable with its reason; the definition has the members the app's shared features call on every other Airbus (visual guidance, taxi turn lead, flight phase, call-out muting); and everything the A330 speaks or formats is tested under a comma-decimal culture.

**Architecture:** A parity table first: every hotkey action and every overridable definition member, the Fenix's and the FlyByWire A32NX's handling beside the A330's. Each gap is then a small addition in the read-outs file, fed by a variable measured live or by a display page plans 08 and 09 already build. Values that need a flight to measure are implemented with the base class's behavior and handed to plan 11.

**Tech Stack:** C# 13 / .NET 10, xUnit.

**Spec:** `docs/a330/spec.md` sections 2.6, 3 (R18), 6; `docs/hotkey-system.md`, `docs/visual-guidance.md`, `docs/taxi-guidance.md`; rules [VAR-8], [VAR-9], [CORE-17], [VG-15].

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply.
- Owned files: `Aircraft/IniA330Definition.Readouts.cs`, `Aircraft/A330/A330Readouts*.cs` (new), tests `tests/MSFSBlindAssist.Tests/A330/A330Readout*Tests.cs`, `A330CultureTests.cs`, `A330HotkeyParityTests.cs`, `A330DefinitionMembersTests.cs`; `docs/a330/evidence/10-parity.md` (new); by the index's named exception, the A330's line in `Services/DefAnnounceMuteSets.cs` if Task 3 needs one.
- Keys that open or feed a display belong to the display's plan (03 the MCDU, 05 the FCU, autopilot and altimeter, 08 the ECAM displays and the ECAM call-out key, 09 the PFD, ND, ISIS, DCDU and the next-waypoint key). This plan adds only what is left.
- A read-out says the aircraft's value as the aircraft shows it; a value read for a key and never spoken by itself is excluded from Ctrl+M ([VAR-9]).
- A number that needs a flight to measure (a visual-guidance bias, a taxi turn lead) is never copied from another aircraft: the base class's behavior stays until plan 11 measures it.

## Measured on 2026-11-20

- **M13: the aircraft publishes its flight phase.** `INI_flight_phase` (lower case),
  `INI_EWD_FLIGHT_PHASE` and `INI_ECAM_PHASE` each read 1 at the gate on external power. Their values
  through a flight are plan 11's.
- **Tape speeds on the ground.** With weights entered, `INI_GREENDOT_SPEED`, `INI_S_SPEED` and
  `INI_F_SPEED` still read 0, while the MCDU's PERF TAKE OFF page shows them (F=147, S=173, O=209 that
  day) and PERF APPR the approach ones. Output Shift+1 to 3 on the ground can say "not available on the
  ground" or, once plan 03 has landed, read the PERF page's values: the owner's call (Task 2).
- Variables for other read-outs found by name that day: `INI_FMS1_message_light` and
  `INI_FMS2_message_light` (an FMS message is waiting), `INI_ATC_MSG`, `INI_COMPANY_MSG_RECEIVED`,
  `INI_ND_MESSAGE`.

## Review Focus

- A key that says nothing at all on the A330 while it speaks on every other Airbus: the parity test fails on an action neither handled nor listed as not applicable (Task 1).
- A weight or fuel figure in the wrong unit: the unit follows the aircraft's own unit setting, read at the time of the key (Task 2 test in kg and lb).
- "5,2 tonnes" under de-DE, or a typed value misread: every sentence builder is run under de-DE and sv-SE (Task 4).
- The take-off call-outs muted by one Ctrl+M row silencing another's ([VAR-8]): each call-out checks its own row and the branch is exempt from the wrap (Task 3 test).

---

### Task 0 [controller]: Claim this plan

As plan 01's Task 0, with this file's path and `docs(a330): claim plan 10` (`Plan: 10`). Start once plan 01 has landed; Task 2's display-fed read-outs wait for plans 08 and 09.

### Task 1: The parity table and its test

**Files:** the evidence file; test `tests/MSFSBlindAssist.Tests/A330/A330HotkeyParityTests.cs`.

- [ ] **Step 1:** The table, from the code: a row every `HotkeyAction` the Fenix definition or the FlyByWire A32NX definition handles (their `HandleHotkeyAction` and hotkey maps), with what each does there and what the A330 does (handled in which plan's file, or missing). A second table: every virtual member of `BaseAircraftDefinition` and `IAircraftDefinition` those two override, and whether the A330 does.
- [ ] **Step 2:** Steps 1-5: `Every_action_the_other_Airbus_aircraft_handle_is_handled_or_listed` (the test holds a list `NotApplicable` of action and reason; an action the A330's dispatcher does not handle and the list lacks fails). It fails now on the missing ones: Tasks 2 and 3 empty the failures. Commit `test(a330): hotkey parity with the other Airbus aircraft`.

### Task 2 [sim]: The missing read-outs

**Files:** Create `Aircraft/A330/A330Readouts.cs`; modify `Aircraft/IniA330Definition.Readouts.cs`; test `A330ReadoutTests.cs`; the evidence file.

**Interfaces:**
- Produces, pure and tested, one a read-out: `internal static class A330Readouts { public static string Fuel(...); public static string GrossWeight(...); public static string Waypoint(...); public static string StallSpeed(...); }`, each taking the values and the unit setting and returning the sentence.

- [ ] **Step 1:** The tape speeds first. Today Output Shift+1, 2, 3, 4 and 6 read green dot, S, F, VLS and the maximum speed from `INI_GREENDOT_SPEED`, `INI_S_SPEED`, `INI_F_SPEED`, `INI_VLS_SPEED` and `INI_MAX_SPEED`; Shift+5 (the stall speed on the other Airbus aircraft) says nothing, and Shift+6 is VFE next on the others. Measured 2026-11-19 on the ground with no weights entered: green dot, S and F read 0, so the keys say "not available". Measure when each fills (after the weights are entered on INIT B, at engine start, or only in the air) and through plan 11's flight, find the stall speed's and VFE next's variables, and record them. A key says "not available" only while the PFD shows no such speed. Then, for each of fuel information (each tank and the total, as the Fenix's key says its own), gross weight, and the active waypoint's information (name, distance, time): find where the aircraft holds the value. Order of sources: a variable the definition already registers; an `INI_*` L:var found by the differential method (change the quantity through the EFB or the MCDU with the owner's go-ahead and see what moves); a display page of plan 08 or 09 (the lower ECAM's FUEL page, the ND's top right corner). Record each source and its unit in the evidence file.
- [ ] **Steps 1-5 per read-out:** tests named for it (`Fuel_says_each_tank_and_the_total_in_the_aircrafts_unit`, `Gross_weight_is_said_in_tonnes_or_thousand_pounds_as_the_aircraft_is_set`, `The_waypoint_read_out_says_no_active_waypoint_when_there_is_none`), de-DE and sv-SE; implement through `HandleReadoutHotkey` and `AddReadoutVariables` (cache-only variables excluded from Ctrl+M); commit `feat(a330): the <name> read-out`.
- [ ] **Step 2:** The key that lists an aircraft's own windows on the other Airbus aircraft (`ShowAircraftWindows`) opens a list of the A330's: MCDU, the four FCU windows, autopilot, altimeter, E/WD, system display, PFD, ND, ISIS, DCDU, EFB, monitor manager; each entry opens its window through the same code its key does. Test: `The_window_list_names_every_A330_window_once`.

### Task 3: The definition's members

**Files:** `Aircraft/IniA330Definition.Readouts.cs`; tests `A330DefinitionMembersTests.cs`.

- [ ] **Steps 1-5, call-out muting ([VAR-8]):** `IsMuteWrapExempt` names the take-off call-out branch, which checks each call-out's own row; tests `Muting_V1_leaves_Rotate_spoken`, `A_muted_flap_row_says_nothing_through_the_wrap`; the A330's line in `Services/DefAnnounceMuteSets.cs` is checked against what `ProcessSimVarUpdate` speaks.
- [ ] **Steps 1-5, flight phase:** `CurrentFlightPhase` from the aircraft's own phase variable if it has one (measure: an `INI_*` phase L:var read on the ground, and through plan 11's flight), mapped to the app's phases with one test a value; without one, the base class's behavior stays and the record says so.
- [ ] **Step 3:** `TakeoffCalloutFeedKey` and `TakeoffCalloutFeedNeeded`: say in the record whether the A330's own V-speed call-outs make them unnecessary (it calls V1, Rotate and V2 itself) or add them as the Fenix has them; the all-aircraft take-off call-out test of plan 01 decides.
- [ ] **Step 4:** `GetVisualGuidanceProfile` and `TaxiTurnLeadSeconds`: add the overrides returning the base values, each with a doc comment saying it is unmeasured, and hand plan 11 the measurement (the method in `docs/visual-guidance.md` and `docs/taxi-guidance.md`: the two biases measured separately, [VG-15]; the taxi lead tuned to the median lead needed, never the largest). When plan 11's numbers arrive, this plan's claimant sets them with one test each.

### Task 4: Everything spoken, under a comma-decimal culture

**Files:** Create `tests/MSFSBlindAssist.Tests/A330/A330CultureTests.cs`.

- [ ] **Steps 1-5:** one theory over every sentence builder the A330 has that formats a number (find them: `git grep -n -E "\\$\"|ToString\\(" MSFSBlindAssist/Aircraft/IniA330Definition*.cs MSFSBlindAssist/Aircraft/A330`): each run under en-US, de-DE and sv-SE with `TestCulture.Run` must give the same text. A builder in another plan's file that fails is fixed by that plan's claimant if the plan is open, else here with a note in the record. Commit `test(a330): spoken text under comma-decimal cultures`.

## In-sim checks (owner)

1. Press each read-out key of the hotkey guide's A330 list once on the ground and once in cruise: none is silent, and fuel and weight match the aircraft's displays.
2. Open the A330's window list and open three windows from it.

## Handoff to plan 12

- **Pilot-facing:** the new read-out keys and the window list. Hotkey guide and `guide/hotkey-reference.md`: every key added by any plan, in the Airbus chapters' order. Changelog `improvement`.
- **Rule proposals:** none expected.
- **For plan 11:** the visual-guidance biases and the taxi turn lead to measure; the flight-phase variable through a whole flight.
- **Frozen files:** `Services/DefAnnounceMuteSets.cs` (done here by exception if needed).

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
