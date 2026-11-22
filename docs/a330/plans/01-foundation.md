# A330 Foundation Implementation Plan (plan 01, first and alone)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** The branch's two commits are reviewed by someone who did not write them and their real findings fixed test-first; the definition is split into one file an owner, with hooks at every shared dispatch point; the tests are split the same way; and the A330 is in every test that sweeps all aircraft. Plans 02 and 04-07 then start in parallel without editing one line twice.

**Architecture:** A review that gets the code, not anyone's conclusions; one fix commit a finding. Then pure moves: `IniA330Definition.Handlers.cs` and `.Variables.cs` keep only the dispatchers and the write primitives, and everything else moves, unchanged, into a partial file named for the plan that owns it. Each dispatcher calls `partial void` hooks in a fixed order, so a later plan adds behavior in its own file. No behavior changes in the moves: the suite is the proof.

**Tech Stack:** C# 13 / .NET 10 Windows Forms, xUnit.

**Spec:** `docs/a330/spec.md` sections 2.6, 3 (R1-R3) and 4; `docs/a330/plans/00-index.md` "File ownership".

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply.
- The review range is `git diff $(git merge-base origin/main HEAD) HEAD` (two commits, 79 files).
- While 01 runs it owns every file those commits touch. Nobody else works on the branch's code until 01 records its base commit.
- A Minor finding is not fixed here: it goes in the execution record with the plan owning its file.
- Tasks 3-5 add no behavior: every existing test passes unchanged, the Ctrl+M dialog lists the same rows in the same order, nothing new is said.
- A moved member keeps its name, signature, body and doc comment. Members move whole; no file gets a second copy.

## Review Focus

- A review that re-reads the commit messages finds nothing: the reviewer gets the diff and the files around it, never this plan's guesses (Task 1).
- The input-event read feature is shared code (`SimConnectManager.InputEventReads.cs`, `Setup.cs`, `Dispatch.cs`, `DataRequests.cs`, `ContinuousBatchLayout.cs`): every aircraft that has input events is checked, not only the A330 (Task 1).
- A hook called in the wrong order changes what is spoken: the flap call-out, the take-off call-outs and the FMA must each be reached exactly as before (Task 3 test).
- An aircraft switch away from the A330 with the EFB window open must close its connection (Task 5 test).
- The all-aircraft sweeps may fail on the A330's labels (two rows of one panel with one name, a combo label that collapses): each failure is a real defect to fix, never a row to exclude (Task 6).

---

### Task 0 [controller]: Claim this plan

**Who:** whoever runs the plan, before any other task; with subagents, the controller itself, never an implementer. A claim counts only once it is on the remote branch.

- [ ] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/feat/inibuilds-a330-fs2024 -- docs/a330/plans/01-foundation.md`. Anything but "not started" and "none" means the plan is taken: stop.
- [ ] **Step 2: Branch and claim.** Branch as in the index's workflow step 1. In this file's "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the commit you branched from. Commit only that change: `docs(a330): claim plan 01`, with the `Plan: 01` trailer.
- [ ] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/feat/inibuilds-a330-fs2024`, `git push <remote> HEAD:feat/inibuilds-a330-fs2024`. If the rebase stops on these lines, someone claimed the plan first: `git rebase --abort` and stop. Task 1 starts only once the push has gone through.

### Task 1: The review

**Files:** none changed; findings go in this file's execution record.

- [ ] **Step 1: Run an independent correctness review** of the range with superpowers:requesting-code-review (or the repo's `code-review` skill at high effort). Give the reviewer the diff, CLAUDE.md, `.claude/rules/inibuilds.md` and the rule files the touched files load, and these areas to read whole: the input-event reads and their request ids 701-890 (`SimConnectManager.InputEventReads.cs` and its four call sites; `InputEventReadRequestIdTests`); the FCU value windows' write and read-back (`SetAndConfirm` and its callers in `IniA330Definition.Handlers.cs`: which thread sends the write, [SIM-20]); the take-off call-out branch of `ProcessSimVarUpdate` and its Ctrl+M rows ([VAR-8], `Services/DefAnnounceMuteSets.cs`); what closes the EFB connection and the MCDU pop-out on an aircraft switch; the altimeter Standard press order (`BaroPresses`, `AltimeterCommandPresses`, `ToggleStandard`); `TransitionAltimetersTests`' new exception; `Settings/UserSettings.cs`' new set; the First Officer's write routes (`FoWriteRoute`, `FoPositionValue`, `IniAirbusActionExecutor`); the EFB agent's click path.
- [ ] **Step 2: Check CLAUDE.md's "Check the whole of what you touched"** over the same range: each spoken or formatted change has a de-DE or sv-SE test; each shared change holds on every aircraft using it.
- [ ] **Step 3: Record every finding** in "Review findings" below (file:line, what fails, the input that shows it, severity Critical / Important / Minor, owning plan), and say which the reviewer verified against the code.

### Task 2: Fix the Critical and Important findings

**Files:** the finding's own; its test in `tests/MSFSBlindAssist.Tests/IniA330DefinitionTests.cs` or a new `tests/MSFSBlindAssist.Tests/A330/A330ReviewTests.cs`.

- [ ] **Steps 1-5 per finding:** a failing test that shows the finding, the fix, the suite green, one commit each: `fix(a330): <what the finding was>` with the `Plan: 01` trailer. A finding that needs the sim to show it goes to its owning plan instead, as a handoff request in this file's record.

### Task 3: Seam: the hooks and their call order

**Files:** Create `Aircraft/IniA330Definition.Hooks.cs`; modify `Aircraft/IniA330Definition.Handlers.cs`, `Aircraft/IniA330Definition.Variables.cs`; test `tests/MSFSBlindAssist.Tests/A330/A330HooksTests.cs`.

**Interfaces:**
- Produces, for every later plan. `IniA330Definition.Hooks.cs` holds only declarations, grouped by owner, each with a one-line comment naming its plan. All are instance `partial void` with no access modifier:
  ```csharp
  // BuildVariables: called in this order, after the base variables.
  partial void AddPanelVariables(Dictionary<string, SimVarDefinition> vars);          // 04
  partial void AddFcuVariables(Dictionary<string, SimVarDefinition> vars);            // 05
  partial void AddFlightDisplayVariables(Dictionary<string, SimVarDefinition> vars);  // 09
  partial void AddEcamVariables(Dictionary<string, SimVarDefinition> vars);           // 08
  partial void AddMcduVariables(Dictionary<string, SimVarDefinition> vars);           // 03
  partial void AddReadoutVariables(Dictionary<string, SimVarDefinition> vars);        // 10
  partial void AddFirstOfficerVariables(Dictionary<string, SimVarDefinition> vars);   // 06

  // HandleUIVariableSet: Fcu, Ecam, FlightDisplay, then Panel (the map's catch-all); stops at the first handled.
  partial void HandleFcuSet(string varKey, double value, SimVarDefinition varDef, SimConnectManager sim, ScreenReaderAnnouncer announcer, ref bool handled);
  // likewise HandleEcamSet (08), HandleFlightDisplaySet (09), HandlePanelSet (04)

  // ProcessSimVarUpdate: Readout, Panel, FlightDisplay, Fcu, Ecam, Mcdu; stops at the first handled;
  // then the silent keys, then the base class.
  partial void ProcessReadoutUpdate(string varName, double value, ScreenReaderAnnouncer announcer, ref bool handled);
  // likewise ProcessPanelUpdate (04), ProcessFlightDisplayUpdate (09), ProcessFcuUpdate (05), ProcessEcamUpdate (08), ProcessMcduUpdate (03)

  // TryGetDisplayOverride: FlightDisplay, Fcu, Panel, Readout, Ecam; the first to set text wins.
  partial void FormatFlightDisplay(string varKey, double value, ref string? text);
  // likewise FormatFcuDisplay (05), FormatPanelDisplay (04), FormatReadoutDisplay (10), FormatEcamDisplay (08)

  // HandleHotkeyAction, after `_sim = simConnect;`: Readout, Fcu, FlightDisplay, Ecam, Mcdu, Efb; stops at the first handled.
  partial void HandleReadoutHotkey(HotkeyAction action, SimConnectManager sim, ScreenReaderAnnouncer announcer, Form parentForm, HotkeyManager hotkeys, ref bool handled);
  // likewise HandleFcuHotkey (05), HandleFlightDisplayHotkey (09), HandleEcamHotkey (08), HandleMcduHotkey (03), HandleEfbHotkey (07)

  // ResetAnnouncementBaselines (contextReset false) and OnSimContextReset (true).
  partial void ResetPanelBaselines(bool contextReset);
  // likewise ResetFlightDisplayBaselines (09), ResetReadoutBaselines (10), ResetFcuBaselines (05), ResetEcamBaselines (08), ResetMcduBaselines (03), ResetReader (02)

  // OnSwitchedAway (new override, Task 5).
  partial void CloseMcduWindows();          // 03
  // likewise CloseEfb (07), CloseEcamWindows (08), CloseFlightDisplayWindows (09), CloseFcuWindows (05), StopReader (02)
  ```
- A hook nobody implements compiles to nothing, so the order above is the whole contract. Each call site carries a one-line comment naming the plan that implements it.

- [ ] **Steps 1-5:** tests `Every_hook_is_declared_once_and_called_once` (source scan of the three files: each declared hook name appears at exactly one call site), `The_dispatchers_still_reach_the_base_class_for_an_unknown_variable`, `An_unknown_hotkey_is_not_handled`; implement the declarations and the calls with the existing bodies still in place between them; suite green; commit `refactor(a330): hooks at the definition's dispatch points`.

### Task 4: Split the definition by owner (pure moves)

**Files:** Create `Aircraft/IniA330Definition.Panels.cs`, `.Fcu.cs`, `.FlightDisplays.cs`, `.Ecam.cs`, `.Mcdu.cs`, `.Efb.cs`, `.Readouts.cs`, `.FirstOfficer.cs`; modify `.Handlers.cs`, `.Variables.cs`.

What moves where, each as the implementation of its hook (the existing `case` bodies become the hook's `switch`, setting `handled = true` where they returned true):

| To | Members |
|---|---|
| `.Panels.cs` (04) | the map-driven part of `HandleUIVariableSet` (from the `_controlsByKey` lookup to its end) as `HandlePanelSet`; the own-panel cases (`A330_FLAPS`, `A330_SPDBRK`, `A330_SPLR_ARM`, `A330_GEAR`, `A330_PARKBRK`, `A330_AUTOBRAKE`); `AutobrakeEvent`, `SetAutobrake`, `ParkingBrakeInput`, `ParkingBrakeRead`; the flap call-out (`_lastFlapIndex`, `FlapDetents`) as `ProcessPanelUpdate`; the flap, slat, gear, trim and landing-elevation display texts and `FormatTrim` as `FormatPanelDisplay`; the generated controls' and own panels' variables as `AddPanelVariables` |
| `.Fcu.cs` (05) | `AutopilotButtons`, `PressAutopilot`, `AltStepInput`, `PressesWithPushAction`, the `A330_SPD_MACH` / `A330_METRIC` / `A330_ALT_STEP` / `A330_TRK_FPA` set cases as `HandleFcuSet`; the four `A330_FCU_*` display texts and `A330_FMA_AP` (`ApStatus`) as `FormatFcuDisplay`; `SpeedManaged`, `LocState`, `ApprState`, `TrkFpaToggle`, `AutopilotRows`, the four `Show*Dialog`, `SetAndConfirm`; the altimeter (`ReadBaroModeAsync`, `ReadAltimeter`, `BaroPresses`, `AltimeterCommandPresses`, `ToggleStandard`, `ShowBaroDialog`, `IsStdMode`, `DescribeAltimeter`); the FCU, autopilot and altimeter hotkey cases (`FCUSet*`, `Toggle*`, `FCU*Pull` / `Push`, `ReadAltimeter`) as `HandleFcuHotkey`; the FCU variables as `AddFcuVariables` |
| `.FlightDisplays.cs` (09) | the FMA (`FmaInputVars`, `_fma*`, `Fv`, `CurrentFma`, `ScheduleFma`, `AnnounceFmaChange`, `FmaMonitorKey`) as `ProcessFlightDisplayUpdate`, `AddFlightDisplayVariables`, `ResetFlightDisplayBaselines`; the five `A330_FMA_*` mode texts as `FormatFlightDisplay`; the `ReadDisplayPFD` and `ReadDisplayND` cases and `PfdNdView` as `HandleFlightDisplayHotkey`; `ReadApproachCapability` |
| `.Ecam.cs` (08) | the `ReadDisplayUpperECAM` and `ReadDisplayLowerECAM` cases and `EcamView` as `HandleEcamHotkey` |
| `.Mcdu.cs` (03) | the `ShowFenixMCDU` case and `CduProfile` as `HandleMcduHotkey` |
| `.Efb.cs` (07) | the `ShowPMDGEFB` case and `ShowEfb` as `HandleEfbHotkey` |
| `.Readouts.cs` (10) | the take-off call-outs (`_takeoffCallouts`, `_calloutOnGround`, `TakeoffMuteKey`, the `Takeoff*Key` cases) as `ProcessReadoutUpdate` and `ResetReadoutBaselines`; the read-out hotkey cases (heading, speed, altitude, V/S, flaps, gear, fuel, altimeter excluded: that is 05's; radios, distances, the five speeds) as `HandleReadoutHotkey`; `FuelSummary`, `ReadSpeed`, `ReadFmsDistance`, `DescribeFmsDistance`, `ReadRadios`; their variables as `AddReadoutVariables` |
| `.FirstOfficer.cs` (06) | `FoWriteRoute`, `FoPositionValue` and the First Officer's read variables as `AddFirstOfficerVariables` |
| stays in `.Handlers.cs` (frozen) | `Num`, `DefaultPushMs`, `CanCalc`, `PushEvent`, `PressByValue`, `SetInputEvent`, the four dispatchers reduced to their hook calls, `On`, `_sim`, `OnDisplayPanelShown`, the `MonitorManager` hotkey case |
| stays in `.Variables.cs` (frozen) | `BuildVariables` reduced to the base variables and the hook calls, `_silentKeys` |

- [ ] **Step 1:** Move the members, one destination file a commit (`refactor(a330): move the <area> into its own file`), building after each.
- [ ] **Step 2:** After the last move: `git diff --stat` shows `.Handlers.cs` and `.Variables.cs` shrinking by what the new files gain; the suite passes with no test changed; a member that would have to change to move is not moved: record it and its reason.

### Task 5: The shared keys, the window press, the Ctrl+M rows by area, and the way out

**Files:** Create `Aircraft/A330/A330SharedKeys.cs`, `Aircraft/A330/A330CalloutRows.cs`, `A330CalloutRows.Mcdu.cs`, `.Ecam.cs`, `.Pfd.cs`, `.Nd.cs`, `.Datalink.cs`; modify `Aircraft/IniA330Definition.Handlers.cs`, `Forms/IniA330/A330MonitorManagerForm.cs`, `MainForm.IniBuilds.cs`, `MainForm.AircraftSwitch.cs`; tests `tests/MSFSBlindAssist.Tests/A330/A330CalloutRowsTests.cs`, `A330WindowPressTests.cs`, `A330SwitchedAwayTests.cs`.

**Interfaces:**
- Produces:
  ```csharp
  namespace MSFSBlindAssist.Aircraft.A330;
  /// Variable keys more than one plan reads; a key lives here only when two plans need it.
  internal static class A330SharedKeys { /* seeded with the keys two of Task 4's files share */ }

  internal static partial class A330CalloutRows
  {
      /// Every row the plans add, in area order; the rows that existed before stay where they are.
      public static IEnumerable<(string Key, string Label)> All => Mcdu.Concat(Ecam).Concat(Pfd).Concat(Nd).Concat(Datalink);
  }
  // A330CalloutRows.Mcdu.cs (likewise Ecam, Pfd, Nd, Datalink), empty until its plan fills it:
  internal static partial class A330CalloutRows
  {
      public static readonly IReadOnlyList<(string Key, string Label)> Mcdu = Array.Empty<(string Key, string Label)>();
  }
  ```
  In `IniA330Definition` (`.Handlers.cs`):
  ```csharp
  /// A display window's press of a panel control: the panel's own write and nothing else. False when the key is no control.
  internal bool PressFromWindow(string varKey, double value, SimConnectManager sim, ScreenReaderAnnouncer announcer);
  /// A panel control's label and its state as the panel shows it; State null while unread. Null when the key is no control.
  internal (string Label, string? State)? DescribeControl(string varKey, SimConnectManager sim);
  /// True while a row the plans added is muted in Ctrl+M.
  internal bool IsCalloutMuted(string key);
  public override void OnSwitchedAway();   // calls the Close... hooks and StopReader, in Task 3's order
  ```
  In `MainForm.IniBuilds.cs`: `partial void StartIniA330Displays();`, called from `MainForm.AircraftSwitch.cs` after a switch and at startup beside the other aircraft's display start (a no-op until plan 02 implements it in `MainForm.IniBuilds.Displays.cs`).

- [ ] **Steps 1-5:** tests `All_lists_the_areas_in_order`, `The_monitor_manager_lists_the_added_rows_after_the_existing_ones`, `An_added_row_with_an_existing_key_is_refused`, `A_muted_added_row_reads_muted`, `A_window_press_takes_the_panels_write_path` (an in-memory selector row: the same input-event value as the panel's press), `A_window_press_of_an_unknown_key_is_false`, `Describe_says_the_label_and_the_cached_state`, `Switching_away_closes_the_efb_connection`; implement; suite green; commit `refactor(a330): shared keys, the window press and the call-out rows`.

### Task 6: The tests split by owner, the rule globs, and the all-aircraft lists

**Files:** move `tests/MSFSBlindAssist.Tests/IniA330DefinitionTests.cs` into `tests/MSFSBlindAssist.Tests/A330/` as `IniA330DefinitionTests.cs`, `A330ControlMapTests.cs`, `A330FcuTests.cs`, `A330FmaTests.cs`, `A330ReadoutTests.cs`, `A330CduScreenReaderTests.cs`; modify `.claude/rules/inibuilds.md` (paths only), `tests/MSFSBlindAssist.Tests/ComboLabelCollapseTests.cs` (the `AllAircraft` list), `tests/MSFSBlindAssist.Tests/FirstOfficer/FirstOfficerWindowsTests.cs`.

- [ ] **Step 1:** Move each test class or method to the file of the plan owning what it tests (the index's "Tests" list); no test body changes. Suite green: the same test count as before.
- [ ] **Step 2:** Add to `.claude/rules/inibuilds.md`'s `paths:` the globs `tests/MSFSBlindAssist.Tests/A330/**`, `MSFSBlindAssist/MainForm.IniBuilds*.cs` (replacing the exact name), `tools/a330-efb-test/**`. Run `ClaudeContextBudgetTests` and `ClaudeRulesHookTests`.
- [ ] **Step 3:** Add `IniA330Definition` to `ComboLabelCollapseTests.AllAircraft` and an `INI_A330` row to `FirstOfficerWindowsTests`. Run every test that reads those lists. Each failure is fixed at its cause (in the generator's label for a generated row: regenerate the map, [INI-2]), one commit a cause: `fix(a330): <the defect the sweep found>`.
- [ ] **Step 4:** Commit `test(a330): tests by owner; the A330 in the all-aircraft sweeps`.

### Task 7: Record the base commit

- [ ] **Step 1:** Whole-plan review (index, "Running a plan with subagents", last bullet). The whole suite, `ClaudeRulesHookTests` included, green.
- [ ] **Step 2:** Fill in "Handoff to plan 12" and the record; push.
- [ ] **Step 3:** One more commit that writes only the "Base commits" line of `00-index.md` (this plan's last code commit) and this plan's landing: `docs(a330): plan 01's base commit`. Push it alone.

## In-sim checks (owner)

1. With the A330 loaded: open the Panels list, the MCDU (Shift+M), the FCU windows (Ctrl+H, S, A, V), the autopilot window (Ctrl+P), the EFB and Ctrl+M. Everything opens and reads as before the split.
2. Switch to another aircraft and back with the EFB window open: no error, and the EFB opens again.

## Handoff to plan 12

- **Pilot-facing** (the A330 is not in `main` yet, so these belong in its one `aircraft` fragment and the
  guide, not in fix fragments): the FCU windows work like the Fenix's (owner, 2026-11-21, tested in the
  simulator): Ctrl+H, S, A and V are the Fenix's windows (its titles, field labels, buttons with no state
  text, its messages, Close; Enter sets and the window stays open), "Set FCU heading" with "Heading Push",
  "Heading Pull", "HDG/VS TRK/FPA"; "Set FCU Speed" with "Speed Push", "Speed Pull", "SPD/MACH"; "Set FCU
  altitude" with "Altitude Push", "Altitude Pull" and the A330's "Metric altitude"; "Set FCU vertical speed"
  with "V/S Push", "V/S Pull"; the A330's real ranges and its track and FPA modes keep their values in those
  words. Ctrl+P is "AP1", "AP2", "ATHR", "APPR", "LOC", then the A330's knob pushes and pulls, "ALT (hold this
  altitude)" and "Read flight modes". No window has Alt letters (which also ends the Alt+S clash, B1). The
  buttons that left the value windows stay in Ctrl+P (LOC, ATHR, ALT, AP1, AP2, APPR) and on the Glareshield
  FCU panel (knob steps, TRK/FPA, SPD/MACH, metric altitude). The input-mode toggle hotkeys Shift+A (AP 1),
  Ctrl+O (AP 2), Ctrl+J (A/THR), Ctrl+L (LOC) and Shift+P (APPR) are gone, as on the Fenix (the in-app hotkey
  guide already says so); the FCU, V/S-FPA and altimeter windows take a
  comma decimal ("-0,5", "0,82", "29,92") on any PC; the knob buttons and Standard in those windows no longer
  repeat themselves 1.2 s later; a heading or V/S typed after pressing the window's TRK/FPA button goes to
  the window the FCU now shows; the flaps, speedbrake, guards and sliders follow a change made elsewhere
  while their panel is open; Ctrl+M no longer lists "Speedbrake lever" or "Landing elevation selected
  (feet)", and the landing elevation is no longer spoken on every change; the First Officer will not tick
  an engine master off without the APU or external power on the buses; the EFB never reads a password
  field's value; picking the A330 from the Aircraft menu after loading it in the simulator under another
  profile finds its controls. Guide (`guide/airbus-aircraft.md`, `guide/hotkey-reference.md`): the A330's FCU
  and Ctrl+P buttons, the five removed toggle hotkeys, and the Ctrl+M rows, where the A330 section lists them.
- **Rule proposal:** `[INI-10] A330 behaviour goes in the owning area file through its hook
  (IniA330Definition.Hooks.cs, called in the order A330HooksTests pins); Handlers.cs and Variables.cs hold
  only the dispatchers and write primitives; a key two areas use lives in A330SharedKeys, which takes
  append-only additions; a map control never reuses a definition key or read (A330VariableOverlapTests).`
  Full text: the seams exist so plans 02-10 never edit one file twice (spec R2); the tests named fail on a
  call out of order, a second use of a key literal, a call-out row that clashes, and a map overlap.
- **Frozen files plan 01 changed:** `MainForm.AircraftSwitch.cs` (the `StartIniA330Displays()` seam call);
  `MainForm.cs` (the same call beside the startup `StartBbs757Displays()`, one line: the startup site is
  there, not in the switch file); `SimConnect/SimConnectManager.InputEventReads.cs`, `.Dispatch.cs`,
  `.Setup.cs` (finding A1); the all-aircraft test lists `ComboLabelCollapseTests`, `FirstOfficerWindowsTests`,
  `SpokenDecimalCultureTests`, `CalcPathProbeOptInTests`, `TakeoffCalloutFeedTests`, `DefAnnounceMuteSetsTests`.
- **Gaps for other plans:** A2 (the input-event catalogue read before the cockpit exists): plan 04's live
  sweep shows it, the file is 12's. C2, C3, C11-C13 (the MCDU pop-out): plan 03, which removes it. C5, C14
  (the chocks and covers press): plan 12, which removes it. Plan 07: whether the EFB's SimBrief and Hoppie
  identifier boxes are plain text fields that speak their value (spec section 4's EFB checklist item 11; sim);
  a disabled or read-only password row passes no `disabled` flag (`coherent-a330-efb-agent.js:437-441`); C15;
  add `tools/a330-efb-test/**` to `.claude/rules/inibuilds.md` once the folder exists (`ClaudeContextBudgetTests`
  refuses a glob that matches no file). Plan 05: the Ctrl+H/S/A/V windows are the Fenix's windows (the owner's
  request; `IniA330FcuValueWindow`, built from `A330FcuWindowSpec`, with a test that reads the Fenix windows'
  own button texts and label names), so a value window gets nothing the Fenix's lacks (the one exception is
  "Metric altitude", the owner's); Ctrl+P keeps the Fenix's order with the A330's extras after; no Alt
  letters; Ctrl+B was not changed. A label opened as Heading or V/S keeps that word if the mode changes while
  the window is open (the value still goes to the mode shown at commit). Plan 10: `TakeoffCalloutFeedKey` and the A330 row of
  `DefAnnounceMuteSetsTests` (A10) are done here. The pull request: "also seen on the HS787": an Aircraft-menu
  switch to it after an in-sim load under another profile still uses the old input-event catalogue, as on
  `main` (`InputEventReads.cs:73` counts read variables, and the HS787 only writes). The Minor rows of
  "Review findings" go to the plan named in their row.

## Execution record

Kept in this file only, so no other plan's push ever touches it.

- **Status:** landed (the two in-sim checks wait on the owner)
- **Claimed by:** kn4iee
- **Worked by:** kn4iee with Claude (superpowers:subagent-driven-development: a fresh implementer a task, a
  task review after each, a final whole-plan review)
- **Started:** 2026-11-21, from 53a20303c
- **Landed:** 2026-11-21; last code commit e5c9d2b6a (the base commit for plans 02 and 04-07). The claim
  went to the branch with the work, not first: Claude held every push for the owner's go-ahead.
- **Tests at landing:** 26,101 passed, 0 failed, the whole suite with `ClaudeRulesHookTests` (2026-11-21);
  25,950 at the start.
- **Tasks:** 0 claim f46629314 (not pushed). 1 review 4991c6e83. 2 the 14 Critical and Important findings,
  one commit each, 209706d1b..9d433be80. 3 hooks 891e7e545, call order pinned a2e302e73. 4 the split
  a2675620f..0b86bd23c, overlap guards 2594d7dcd. 5 a496c0a34. 6 546619daa, b42d3c2e6 (the sweeps found
  nothing to fix). 7 the final review's fixes 2bf4694ba..309ed6e4d. The owner's request after the plan (FCU
  windows and keys like the Fenix's) e2135a7ec, 2b5ae3772, e5ed5f503, and after the owner tried it in the
  simulator, the value windows made the Fenix's own 834532120, e5c9d2b6a, each reviewed clean. A scratch snapshot of every variable
  definition, panel, Ctrl+M row and display text was byte-identical across Tasks 3-7 (only the order the
  variables are added in moved, in Task 4).
- **Waiting on the owner:** the two in-sim checks above; the rulings below and "Changed from the
  plan" are Claude's, the owner's to reverse.
- **In-sim results:** not run (no simulator in this session).
- **Changed from the plan:** the review covered four code commits, not two (the branch had grown);
  `BuildVariables` keeps the base variables LAST, as before, not "after the base variables"; its local helpers
  became members of Variables.cs (one renamed `OnRequestVar`: a rule scan refuses a method named
  `OnRequest` there); `OnSwitchedAway` came in Task 3 so every hook had its call site; the `Reset*` hooks
  have two call sites (false and true); `EcamView` and `PfdNdView` were one declaration and were split;
  Task 2's tests were split by owner in Task 6 with the rest; the `tests/.../A330/**` glob came in Task 2;
  `tools/a330-efb-test/**` waits for plan 07; one startup line in `MainForm.cs`; two hooks the plan did not
  list: `CloseReadouts()` (10) on the way out, and an `IsMuteWrapExempt` dispatcher with hooks for 10, 04,
  09, 08 and 03 (the final review: a later plan had no seam for either); `A330SharedKeys` takes append-only
  additions from the plan that creates a key's second use (frozen otherwise); `DescribeControl` gives a push
  key an empty state, never "unread"; the A330 also joined four more all-aircraft lists (spec R3); at the
  owner's request (2026-11-21) the FCU value windows became the Fenix's windows (their own window class,
  with "Metric altitude" as the one A330 extra), Ctrl+P took the Fenix's order with the A330's extras after,
  no window has Alt letters, and the five autoflight toggle hotkeys the Fenix does not have are removed (plan
  05's files and the in-app hotkey guide, plan 12's, changed here, after plan 01 landed, at the owner's
  word; the A330 has no EXPED or disconnect buttons, so Ctrl+P has none).
- **Rulings made while working** (Claude; the owner may reverse): the claim and the work stay local until
  the owner says push; the hook order for the variables is the plan's (Ctrl+M sorts by label and no
  definition name matched a map read, and guard tests now fail on any overlap); a row added twice, or
  clashing with a variable, throws when the Ctrl+M list is built, and tests build the real list; the
  credential finding (C4) was fixed with a test of the agent's source order until plan 07's harness exists;
  the take-off feed contract (A3) was fixed here although plan 10 lists its member; the MCDU pop-out (C2, C3)
  and the chocks and covers press (C5) were handed to the plans that remove them, the branch being
  unreleased.

### Review findings

Task 1, 2026-11-21: three independent reviewers (shared SimConnect code; the definition; the First Officer,
EFB and MCDU) read `git diff 7825d724f 53a20303c` (the merge base with main to the branch tip: four code
commits, 79 files) with CLAUDE.md and the rule files, never this plan. "Verified" = the reviewer traced the
path in the code; "sim" = only the simulator can show the effect. Critical and Important rows marked "Task 2"
are fixed there, test first; the others are handed to the plan named.

| # | Sev | Where | What fails (input) | Owner | Verified |
|---|---|---|---|---|---|
| B1 | Critical | `IniA330Definition.Handlers.cs:503,508` | Ctrl+P: "&Speed pull" and "Altitude pu&sh" share Alt+S; from the opening focus Alt+S pulls the speed knob | 05; Task 2 | yes |
| A1 | Important | `SimConnectManager.InputEventReads.cs:73-82` | the A330 loaded in the sim under another profile, then picked from the Aircraft menu: the old catalogue is kept, every input-event control says "unavailable" | 12 (frozen file); Task 2 | yes |
| A2 | Important | `SimConnectManager.Dispatch.cs:51`, `Setup.cs:909-923` | the catalogue re-read at AircraftLoaded may come before the cockpit exists; nothing retries | 04 (its live sweep shows it), file 12 | no; sim |
| A3, B10 | Important | `IniA330Definition.Handlers.cs:213-227`, `Variables.cs:352-357` | [TKO-5]: hand-kept mute keys; the per-frame airspeed feed is never paused, for the whole flight | 10; Task 2 | yes |
| A4, B4 | Important | `IniA330Definition.Variables.cs:216-223` | "Speedbrake lever" Ctrl+M row mutes nothing ([VAR-9]) | 04; Task 2 | yes |
| B2 | Important | `IniA330Definition.Handlers.cs:661,667,792,797,860,865` | [CORE-17]: FPA "-0,5" on a comma PC writes -5.0; Mach "0,82" and inHg "29,92" refused | 05; Task 2 | yes |
| B3 | Important | `IniA330Definition.Variables.cs:65,181,182,195,232` | [VAR-7]: four pairs of batched variables share a name; the collision test never runs on the A330 | 04/09; Task 2 | yes (drift: sim) |
| B5 | Important | `tools/a330-gen/generate_a330_map.py:96,235` | `status(..., [])` gets Off/Lit: "Landing elevation selected (feet)" is spoken on every change, "Off"/"Lit" at 0/1 ft | 04; Task 2 | yes |
| B6 | Important | `IniA330Definition.Handlers.cs:657-658` | Ctrl+B Standard: the dialog's 1.2 s read-back and the button's own verdict both speak, each cutting the other off ([CORE-7]) | 05; Task 2 | yes |
| B7 | Important | `IniA330Definition.Handlers.cs:763-766,785-789,826-835,853-856` | FCU dialog knob buttons repeat their label 1.2 s later ("Heading pull (selected) Pull") and cut off the flight-mode call-out | 05; Task 2 | yes (overlap: sim) |
| B8 | Important | `IniA330Definition.Handlers.cs:760,774,850,858-867` | Heading and V/S dialogs fix HDG-V/S or TRK-FPA at open; after their own TRK/FPA press a typed value goes to the wrong dial and is reported set | 05; Task 2 | yes |
| B9 | Important | `IniA330Definition.Handlers.cs:240,249` | flaps, speedbrake, sliders and guards never refresh while their panel is open; a stale combo cannot re-pick the shown item | 04; Task 2 | yes |
| B11 | Important | `tests/.../IniA330DefinitionTests.cs:119-123` | no comma-decimal culture test for anything the A330 speaks (altimeter, Mach, FPA, trim, distance, fuel) | 05/10; Task 2 | yes |
| C1 | Important | `IniAirbusChecklistDefinitions.cs:300-304` | a hand tick of "Engine n master: OFF" cuts the engine with no APU or external power; the flow and the other Airbus profiles refuse | 06; Task 2 | yes |
| C2 | Important | `IniA330CduForm.cs:154`, `A330CduPopout.cs:96-157` | the MCDU pop-out keeps moving the cursor and taking the foreground for up to 30 s after Esc or a switch | 03 (removes the pop-out) | yes; sim |
| C3 | Important | `A330CduPopout.cs` | a minimized pop-out stops the MCDU for the session; reopening the window does not help | 03 (removes the pop-out) | no |
| C4 | Important | `coherent-a330-efb-agent.js:436,440` | a disabled or read-only password field is read aloud with its value: the read-only check runs before the mask | 07; Task 2 | yes (field state: sim) |
| C5 | Important | `coherent-a330-efb-agent.js` | a chock or cover press is confirmed twice (the page's notice and the row's new label) ([CORE-7]) | 12 (removes the press) | yes; sim |
| A5 | Minor | `SimConnectManager.DataRequests.cs:307-312` | a forced read of an InputEvent variable never re-fires an unchanged value | 12 | yes |
| A6 | Minor | `SimConnectManager.InputEventReads.cs:22,56,114` | no guard against more than 190 reads in flight | 12 | yes |
| A7 | Minor | `SimConnectManager.cs:1288-1298` | `ForgetInputEventReads` runs only when a MobiFlight module object exists | 12 | yes |
| A8, B14 | Minor | `IniA330Definition.Handlers.cs:192,272-282` | the FMA settle timer and read-back tasks are not stopped on a switch or at exit | 09 (its close hook); Task 5 adds OnSwitchedAway | yes |
| A9 | Minor | `Forms/IniA330/A330MonitorManagerForm.cs:21-27` | the mute list is captured once, against the base form's re-read contract | 01 Task 5 (rewrites that constructor) | yes |
| A10 | Minor | `tests/.../DefAnnounceMuteSetsTests.cs:17-51` | no test of the INI_A330 mute mapping or the A330's mute paths | 10 | yes |
| A11 | Minor | `.claude/rules/simconnect-data.md:29,45`, `Services/DefAnnounceMuteSets.cs:24-25` | INI-3 sits under a "Mirrored from" header; SIM-6 omits 701-890; a comment names the wrong mute mechanism | 12 | yes |
| B12 | Minor | `IniA330Definition.Handlers.cs:258-268` | the FMA says "OP DES" in an open climb until the first hotkey or display panel (`_sim` null) | 09 | yes |
| B13 | Minor | `Aircraft/A330/A330Fma.cs:114` | landing capability drops out during LOC and G/S tracking, then is called again at LAND | 09 | yes |
| B15 | Minor | `IniA330Definition.Handlers.cs:148-152` | `SetAutobrake` presses a level that is already armed (which disarms it) | 04 | yes |
| B16 | Minor | `IniA330Definition.Handlers.cs:798-816` | the Speed dialog's warnings are cut off by its own verdict | 05 | yes |
| B17 | Minor | `IniA330Definition.Handlers.cs:720-722` | `SetAndConfirm` sends its write from a pool thread | 05 | yes |
| B18 | Minor | `IniA330Definition.Handlers.cs:621` | Standard is refused without MobiFlight, though its first press is an input event | 05 | yes |
| B19 | Minor | `IniA330Definition.Handlers.cs:126-128` | momentary panel buttons with no readable state give no confirmation (proposal) | 04 | yes |
| B20 | Minor | `HotkeyGuides/iniBuilds_A330_Hotkeys.txt:107-141` | the hotkey guide leaves out Ctrl+B | 12 | yes |
| B21 | Minor | `IniA330Definition.Handlers.cs:687,777,818,844,869` | the FCU and Baro value dialogs are not tracked windows | 05 | yes |
| B22 | Minor | `IniA330Definition.Handlers.cs:726` | the heading read-back is compared on a line, not around the compass (359 vs 0) | 05 | yes |
| B23 | Minor | `IniA330Definition.Variables.cs:101` | the First Officer cannot silence the definition's refusals, though the comment says it does | 06 | yes |
| B24 | Minor | `IniA330Definition.Variables.cs:299` | wording: "APU start start light" | 04 | yes |
| C6 | Minor | `IniAirbusActionExecutor.cs:94-101` | an ECAM page key is pressed while the shown page is unread | 06 | yes |
| C7 | Minor | `IniA330Definition.Variables.cs:111-112` | the autobrake toggles, but its First Officer write route says it does not | 06 | yes |
| C8 | Minor | `IniA330Definition.Handlers.cs:26-32` | a held T.O CONFIG press is never released at app exit | 06 | yes |
| C9 | Minor | `IniA330Definition.Handlers.cs:120` | toggle-mode controls write the read value, not `InputFor` ([INI-1]) | 04 | yes |
| C10 | Minor | `tests/.../TransitionAltimetersTests.cs:86-94` | the A330 exception does not pin `AltimeterWritesVerified == false` | 12 (shared test) | yes |
| C11 | Minor | `A330CduPopout.cs:106,168` | the camera stays on the MCDU close-up when the first camera read fails | 03 | yes |
| C12 | Minor | `A330CduPopout.cs:355-359` | the pop-out is never closed | 03 | yes; sim |
| C13 | Minor | `IniA330CduForm.cs:273-301,359-369` | MCDU keys can still be typed after the window is disposed | 03 | yes |
| C14 | Minor | `coherent-a330-efb-agent.js:519-530` | the cover press checks only "on ground", not engines or movement | 12 (removes the press) | yes |
| C15 | Minor | `coherent-a330-efb-agent.js:869-871,962,985-997` | a press can go to a different element right after a re-stamp | 07 | yes |

The reviewers' full reports, with each finding's sequence and suggested fix, were kept beside the work and
summarised here.
