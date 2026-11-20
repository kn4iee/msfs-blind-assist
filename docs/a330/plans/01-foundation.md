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

- **Pilot-facing:** nothing new; any fix from Task 2 that a pilot would notice, as its changelog line.
- **Rule proposal:** none expected; a measured trap from the review proposes its line.
- **Frozen files:** the seam calls made in `MainForm.AircraftSwitch.cs`; the two test lists.

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

### Review findings

(Task 1 fills this in: file:line, what fails, the input that shows it, severity, owning plan, verified or not.)
