# A330 FCU and Autopilot Implementation Plan (plan 05, early)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** The FCU value windows (Ctrl+H, S, A, V), the autopilot window (Ctrl+P) and the altimeter window (Ctrl+B) are proven and tested, say each value's managed, selected or dashed state as the FCU shows it, show every button's state, call out a knob turned on hardware, and give the app's automation the autopilot members it uses on every other Airbus.

**Architecture:** What each window computes moves out of the dialog code into pure, tested planners (`A330FcuEntry`: what to write for a typed value; `A330FcuState`: what the FCU shows for each window). Writes stay on the definition's existing paths, sent from the UI thread. The altimeter's Standard press keeps only the presses that were measured to work. Knob call-outs use the app's shared FCU value announcer.

**Tech Stack:** C# 13 / .NET 10 Windows Forms, xUnit.

**Spec:** `docs/a330/spec.md` sections 2.3, 3 (R12), 6 (M7); `docs/ini-a330.md` "FCU", "FCU dialogs", "Altimeter STD"; rules [INI-1], `.claude/rules/fcu-callouts.md`, `autopilot-engage-call-sites.md`, [CORE-17], [SIM-20].

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply.
- Owned files: `Aircraft/IniA330Definition.Fcu.cs`, `Aircraft/A330/A330Fcu*.cs` (new), `Forms/IniA330/IniA330AutopilotWindow.cs`, tests `tests/MSFSBlindAssist.Tests/A330/A330Fcu*Tests.cs`, `A330Autopilot*Tests.cs`; `docs/ini-a330.md`'s FCU, FCU dialogs and Altimeter STD bullets; `docs/a330/evidence/05-fcu-autopilot.md` (new).
- The read-out hotkeys (heading, speed, altitude, V/S spoken on a key) are plan 10's file; this plan gives them `A330FcuState` to say.
- Every write is sent from the UI thread; a delay is awaited there ([SIM-20]).
- A typed value is parsed with `NumberParsing.TryParseTyped` ([CORE-17]) and written with invariant fixed-point formatting (VAR-5).
- No EXPED: the aircraft has no such control.

## Measured on 2026-11-20

- **Task 2 has its variables.** The aircraft publishes what each FCU window shows:
  `INI_FCU_SPD_DASHED`, `INI_FCU_SPD_DOT`, `INI_FCU_HDG_DASHED`, `INI_FCU_HDG_DOT`, `INI_FCU_ALT_DOT`,
  `INI_FCU_VS_DASHED`. Measured at the gate: flight director off, all 0 (speed 100, heading as selected);
  captain's flight director on with a flight plan and V2 entered, speed dashed 1 and dot 1, heading
  dashed 1 and dot 1, altitude dot 0, V/S dashed 1. Still to measure, in the air: each one after a pull
  and a push of its knob, and the altitude dot in a managed climb.
- The FCU's own display is not read by the display reader (spec section 2.1): the windows stay on the
  variables.
- **Task 6, the first officer's knob (M7, on the ground): done.** `AIRLINER_FO_BARO_SET_PULL` set 1 then
  0 gave `XMLVAR_Baro2_Mode` 3 (Standard); `AIRLINER_FO_BARO_SET_PUSH` the same way gave 0 (QNH). The
  third altimeter has `XMLVAR_Baro3_Mode`. Standard in a climb stays with plan 11.
- With the flight director on, the PFD drew the armed modes ALT and NAV as text (plan 09).

## Review Focus

- "0,82" typed for Mach under a comma-decimal culture must set Mach 0.82, never 82 (Task 1 test under de-DE and sv-SE).
- A speed typed in knots while the FCU shows Mach (and the other way round): the unit is swapped first and the value read back in the unit typed (Task 1).
- A value window confirming what it wrote rather than what the FCU then shows: the confirmation is the fresh read-back, and a mismatch is said ("Heading set to 270, not 090") (Task 1).
- A hardware knob turned one click a second for ten seconds must not speak ten times over itself, and BA's own write must not be called out as a knob turn (Task 5).
- The automation engaging the autopilot on the ground or twice (Task 4: the same guards as the other Airbus definitions).

---

### Task 0 [controller]: Claim this plan

As plan 01's Task 0, with this file's path and `docs(a330): claim plan 05` (`Plan: 05`). Branch from the base commit the index records for early plans.

### Task 1: The value windows' entries, planned and tested

**Files:** Create `Aircraft/A330/A330FcuEntry.cs`; modify `Aircraft/IniA330Definition.Fcu.cs`; test `tests/MSFSBlindAssist.Tests/A330/A330FcuEntryTests.cs`.

**Interfaces:**
- Produces:
  ```csharp
  namespace MSFSBlindAssist.Aircraft.A330;
  internal enum A330FcuWindow { Heading, Speed, Altitude, VerticalSpeed }
  /// What one typed entry does: the presses before the write (a unit or mode swap), the variable and value written, and what to read back.
  internal sealed record A330FcuPlan(IReadOnlyList<string> PressesFirst, string WriteVariable, double WriteValue, string ReadBackKey, string Spoken);
  internal static class A330FcuEntry
  {
      /// Null with `error` set when the text is not a value this window takes.
      public static A330FcuPlan? Plan(A330FcuWindow window, string typed, bool machShown, bool trackFpaShown, out string? error);
      public static string Confirm(A330FcuWindow window, double readBack, A330FcuPlan plan);   // "Heading 270", or "Heading set to 270, not 090"
  }
  ```
  Ranges and steps as the windows enforce them today (read them from the `Show*Dialog` bodies; do not widen): heading and track 0-359 with 360 said as 000; speed 100-399 kt or Mach 0.10-0.99 (a value under 1 is Mach); altitude 100-49000 in the knob's step; V/S in hundreds, FPA in tenths of a degree.

- [ ] **Steps 1-5:** tests: `Heading_360_is_written_as_0`, `Mach_is_recognised_by_its_value_and_swaps_the_unit_first`, `A_comma_decimal_Mach_parses_under_de_DE_and_sv_SE`, `An_FPA_is_written_to_the_FPA_dial_when_TRK_FPA_is_shown`, `Out_of_range_is_refused_with_the_windows_words`, `A_read_back_that_differs_is_said`; implement; the four `Show*Dialog` bodies call `Plan` and `Confirm`, and `SetAndConfirm` sends its write and awaits its read-back on the UI thread; commit `refactor(a330): the FCU windows' entries planned and tested`.

### Task 2 [sim]: Managed, selected, dashed

**Files:** Create `Aircraft/A330/A330FcuState.cs`; modify `Aircraft/IniA330Definition.Fcu.cs`; test `A330FcuStateTests.cs`; the evidence file.

**Interfaces:**
- Produces: `internal static class A330FcuState { public static string Describe(A330FcuWindow window, Func<string, double?> read); }`: what the FCU's window shows, in the pilot's words: "Heading 270, selected", "Speed managed", "Altitude 5000, managed", "V/S dashes" (plan 10's read-out hotkeys and this plan's windows both say it).

- [ ] **Step 1:** Measure which `INI_*` L:vars say, for each FCU window: a value is shown or dashed, and the managed dot is lit. Method: with the owner's go-ahead, push and pull each knob (the definition's presses), snapshot the `INI_*` L:vars the definition already registers plus any whose name contains `FCU`, `MANAGED`, `DASH` or `DOT` before and after, and record what moved in the evidence file.
- [ ] **Step 2:** Steps 1-5: `Each_window_says_value_and_managed_or_selected`, `A_dashed_window_says_dashes_and_no_number`, `Track_and_FPA_are_named_when_shown`, `Unread_state_says_the_value_alone`, each under de-DE; register the variables through `AddFcuVariables`; the value windows' titles and the FCU panel's rows use `Describe`; commit `feat(a330): the FCU windows say managed, selected or dashed`.
- [ ] **Step 3 (review, 2026-11-22: the A330 never says managed or selected):** today `INI_SPEED_MANAGED` is a silent cache, the dot and dash variables are not registered, and a speed knob push or pull changes no flight-mode word, so nothing is heard. The variables exist by name (`INI_FCU_SPD_DOT`, `INI_FCU_HDG_DOT`, `INI_FCU_ALT_DOT`, `INI_FCU_SPD_DASHED`, `INI_FCU_HDG_DASHED`, `INI_FCU_VS_DASHED`; Step 1 measures them). Steps 1-5: each window's change between managed and selected is said in the Fenix's sentence ("FCU SPEED Mode: Managed", "FCU HEADING Mode: Selected"), as the A330's FCU follows the Fenix's (owner, 2026-11-21), with its own Ctrl+M row, the first reading silent, and a change BA's own press made said once (the press's read-back, [CORE-7]); the evidence file lists the A32NX's sentence ("Managed speed") beside it. Tests `A_speed_knob_push_says_managed`, `A_heading_knob_pull_says_selected`, `The_first_reading_is_silent`, `A_muted_row_says_nothing`, de-DE. Commit `feat(a330): managed and selected said as on the Fenix`.

### Task 3: The autopilot window shows every button's state

**Files:** `Forms/IniA330/IniA330AutopilotWindow.cs`, `Aircraft/IniA330Definition.Fcu.cs`; test `A330AutopilotWindowTests.cs`.

- [ ] **Steps 1-5:** tests on in-memory rows: `AP1_AP2_and_ATHR_say_off_armed_or_engaged`, `LOC_and_APPR_say_armed_or_active_from_the_flight_modes`, `A_row_with_no_readable_state_shows_its_label_alone`, `A_press_is_not_spoken_again` (CORE-7: the screen reader reads the button's new text); implement: each row's state from the autopilot variables and `CurrentFma()`'s armed and active words (LOC: "armed" while the armed line holds LOC, "active" while lateral starts with LOC; APPR likewise for G/S or the approach capability line); commit `feat(a330): the autopilot window shows each button's state`.

### Task 4: The autopilot members the app's automation uses

**Files:** `Aircraft/IniA330Definition.Fcu.cs`; test `tests/MSFSBlindAssist.Tests/A330/A330AutopilotEngageTests.cs`.

**Interfaces:**
- Produces the overrides the other Airbus definitions have (same signatures as `BaseAircraftDefinition` declares them): `EngageAutopilot`, `IsAutopilotEngaged`, `AutopilotEngageRetryIntervalMs`, `AutopilotEngageWaitsForState`.

- [ ] **Steps 1-5:** tests modelled on the Fenix's and the Headwind A330's autopilot-engage tests: `Engage_presses_AP1_once_when_it_is_off`, `Engage_does_nothing_when_an_autopilot_is_on`, `IsAutopilotEngaged_reads_AP1_or_AP2`, `The_retry_waits_for_the_state_to_read`; implement with `PressAutopilot("A330_AP1")` and the `A330_AP1_ON` / `A330_AP2_ON` reads; commit `feat(a330): autopilot engage for the app's automation`. The press in the air is unproven (M7): the plan lands with this marked "waiting on plan 11" in the record.
- [ ] **Step 2 (review, 2026-11-22: the words differ from the other Airbus aircraft):** the A330 says "Autopilot 1: Engaged" and "Autothrust: Engaged" (`IniA330Definition.Fcu.cs`); the Fenix says "FCU AUTOPILOT 1: Activated", the A32NX "Autopilot 1: On" and "Autothrust: Active". Steps 1-5: the autopilot and autothrust announcements take the Fenix's sentences, as the FCU follows the Fenix (owner, 2026-11-21), the A32NX's listed beside them in the evidence file for the owner; a disconnect is said as the Fenix says it. Tests `Autopilot_1_on_is_said_as_the_Fenix_says_it`, `Autothrust_off_is_said_as_the_Fenix_says_it`. Commit.
- [ ] **Step 3:** The V/S sentence builders (`IniA330Definition.Fcu.cs`, the V/S read-back and the knob call-out) format with the PC's culture (`{x:0}`): under sv-SE a negative value takes the U+2212 minus. They format with the invariant culture ([CORE-21]); test `A_negative_VS_is_said_with_a_plain_minus_under_sv_SE` ("-700"). Commit.

### Task 5: A knob turned on hardware is called out

**Files:** Create `Aircraft/A330/A330FcuCallouts.cs`; modify `Aircraft/IniA330Definition.Fcu.cs`; test `A330FcuCalloutsTests.cs`.

- [ ] **Step 1:** Read `.claude/rules/fcu-callouts.md` and the shared announcer it governs, and the FlyByWire A32NX's use of it. The A330 uses the shared class unchanged; if it needs a line in a shared file, that is a handoff to plan 12 and this task's wiring waits for it.
- [ ] **Steps 1-5:** tests: `A_dial_that_settles_is_spoken_once_with_its_unit` ("Heading 270"), `A_dial_still_turning_is_not_spoken`, `BAs_own_write_is_not_a_knob_turn` (the value windows mark their write), `Each_dial_has_its_Ctrl_M_row_and_a_muted_row_says_nothing`, `The_first_reading_after_a_flight_load_is_silent`, de-DE; implement through `ProcessFcuUpdate`; commit `feat(a330): FCU knob call-outs`.

### Task 6 [sim]: The altimeter's Standard press, measured

**Files:** `Aircraft/IniA330Definition.Fcu.cs`; test `A330FcuTests.cs` (the existing altimeter tests); the evidence file.

- [ ] **Step 1:** On the ground, both knobs: the input event pressed by value (pull to STD, push to QNH) on `AIRLINER_CPT_BARO_SET_*` and the first officer's; read `XMLVAR_Baro1_Mode` and the first officer's mode after each. Then each fallback in `BaroPresses` and `AltimeterCommandPresses` alone: does it change the mode? Record.
- [ ] **Step 2:** Keep only presses that were measured to work; delete the rest and their "not measured" comments; `ToggleStandard` says which side it could not set. Steps 1-5 with a fake press recorder: `Standard_presses_only_measured_presses`, `A_side_that_does_not_follow_is_named`.
- [ ] **Step 3:** Ctrl+B's typed QNH: all three altimeters read back (captain, first officer, standby), in hectopascals and inches, under de-DE.

## In-sim checks (owner)

1. On the ground: Ctrl+H 270, Ctrl+S 250, Ctrl+A 5000, Ctrl+V -700: each confirmed with what the FCU shows; the autopilot window's rows read their states.
2. Turn the heading knob on your hardware (or the sim's key binding): the new heading is spoken once when you stop.
3. In flight (plan 11 flies it): AP 1, A/THR, LOC and APPR from the autopilot window; Standard passing the transition altitude.

## Handoff to plan 12

- **Pilot-facing:** FCU windows say managed, selected or dashed; knob call-outs with their Ctrl+M rows; the autopilot window's states. Guide: the A330's autopilot section; hotkey guide unchanged unless a key was added; changelog `improvement`.
- **Rule proposals:** INI-16: the altimeter's Standard is pressed only by the presses measured to work (name them), per side.
- **Frozen files:** the shared FCU announcer's registration, if Task 5 Step 1 found one is needed.
- **For plan 11:** M7's list: AP 1, AP 2, A/THR, LOC, APPR pressed in the air and read back; Standard in the climb, on both knobs.

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
