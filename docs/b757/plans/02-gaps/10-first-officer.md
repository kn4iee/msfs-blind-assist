# BlueBird 757 First Officer Implementation Plan (plan 10, later: new features)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A First Officer for the BlueBird 757 in the shared First Officer window (Shift+C), at parity with the other aircraft: first BlueBird's Normal Checklist as read-back checklists that tick themselves from the cockpit, then action groups the First Officer can carry out, then flows and the flight-phase reminders.

**Architecture:** The shared window `FirstOfficerForm<TExec,TState>` builds everything from an `IFoProfile` (`docs/first-officer.md`). The 757 follows the MD-11's First Officer (`FirstOfficer/MD11/`), the closest model: it replays cockpit clicks through the calculator and writes through the aircraft definition. Stage 1 needs no writes: an `LVarStateEvaluator` subclass polls the map controls the checklist reads (they are OnRequest L:vars keyed by node) and an executor that does nothing. Stage 2 adds an awaitable, silent write path on the definition (`FoSetAsync`, `FoPressAsync`), because the panel path is fire-and-forget and speaks for itself. Stage 3 adds flows and a phase monitor.

**Tech Stack:** C# 13 / .NET 10 Windows Forms, xUnit.

**Spec:** `docs/b757/spec-gaps.md` sections 4 (R68, R70), 8 (Q6), 2 (the A-to-B table); BlueBird's Normal Checklist v1.01 (owner's copy; the manuals research's checklist table pairs items with values) and Complete Flight Guide flows (FG 1379-2612); `docs/first-officer.md` and its rule files (FO-1 … FO-26, FOB-*); the MD-11 First Officer files as the model.

## Global Constraints

- Owned files: `FirstOfficer/BBS757/**` (new), `Aircraft/BBS757Definition.FirstOfficer.cs` (new; implements plan 01's `AddFirstOfficerVariables`), `Forms/FirstOfficer/FirstOfficerWindows.cs` (its one 757 row), the `paths:` list of `.claude/rules/b757.md` (add `"MSFSBlindAssist/FirstOfficer/BBS757/**"` and `"tests/MSFSBlindAssist.Tests/FirstOfficer/BBS757/**"`), tests `tests/MSFSBlindAssist.Tests/FirstOfficer/BBS757/**` (new) and the cross-profile FO tests the 757 must join: `FirstOfficer/FirstOfficerWindowsTests.cs`, `FoDepartureGroupsTests.cs`, `BeforeStartAcarsReadbackTests.cs` (stage 1); `FlowStepRunOnlyIfSentIdTests.cs`, `FlowStepDependencyIdTests.cs`, `FoChecklistRefinementTests.cs`, `FoCutGuardSweepTests.cs`, `FoEngineAndGroundGuardTests.cs`, `EngineStartSelectorLatchTests.cs` (stages 2-3).
- Registration only in `FirstOfficerWindows` (FO-24): `["BBS_757"] = Of<BBS757Definition>("BlueBird 757 First Officer", (b757, sc, a, s) => Window(new FirstOfficer.BBS757.Bbs757FoProfile(b757), sc, a, s))`. MainForm needs no change.
- Group ids are the usual Boeing ones already in `FoDepartureGroups` (`PREFLIGHT_CL`, `BEFORE_START_CL`, `BEFORE_TAXI_CL`, `BEFORE_TAKEOFF_CL`, `AFTER_TAKEOFF_CL`, `DESCENT_CL`, `APPROACH_CL`, `LANDING_CL`, `AFTER_LANDING_CL`, `SHUTDOWN_CL`, `SECURE_CL`); `BEFORE_START_CL` ends with the Captain reminder `BSC_ACARS` "Start ACARS".
- Every state the checklist reads comes from BlueBird's own variables, never the stock ones it overrides (B757-1: the parking brake is `L:B757_FC_PARK_BRAKE`); an unread value is NaN, never 0 (FO-20); gear lines read lever and legs, NaN when unknown, read-only (FO-13, FOB-5).
- `*_CL` groups have no actions (FO-3); the landing autobrake is a Captain item (FO-14); no FMC programming (FO-2).
- Fuel control switches differ by engine: PW `L:BBS_fuelcut_PW1/2`, RR `L:BBS_fuel_RR`; read the variant from `L:B757_ENG_VARIANT` (0 RB211, 1 PW2000).
- Stage 2 writes: through `FoSetAsync` / `FoPressAsync` only, refused when `CalcWriteCanLand` is false, unique calculator writes ([SIM-17], [VAR-4]), never from a pool thread to `SendEvent` ([SIM-20]); cut guards and `LeaveAloneWhen` on fuel, IRS, battery and hydraulics (FO-25).
- Variables added: only what `AddFirstOfficerVariables` needs that no other plan registers (expected: none beyond a few OnRequest reads); the total stays under 900.

## Review Focus

- A checklist line whose variable is unread stays unticked and never reverts (NaN skips both), Task 2 test.
- The fuel control line reads the right switch for a PW or an RR aircraft (Task 2 test with both variants).
- The gear line needs the lever and all three legs; any unread → NaN (Task 2 test).
- A stage 2 action that BlueBird refuses (the read-back differs) reports the failure and does not tick (FO-9), Task 5 test.
- Opening Shift+C on the 757 with no SimConnect connection opens the window offline, as on the other aircraft (Task 3 test through `FirstOfficerWindowsTests`).

---

### Task 1 [stage 1]: Profile, evaluator, empty executor and managers

**Files:** Create `FirstOfficer/BBS757/Bbs757FoProfile.cs`, `Bbs757FoStateEvaluator.cs`, `Bbs757FoActionExecutor.cs`, `Bbs757FoAutoManager.cs`, `Bbs757FoFlightPhaseMonitor.cs`; tests `tests/.../FirstOfficer/BBS757/Bbs757FoProfileTests.cs`.

**Interfaces:**
- `Bbs757FoProfile : IFoProfile<Bbs757FoActionExecutor, Bbs757FoStateEvaluator>` taking the `BBS757Definition`: `Title` "BlueBird 757 First Officer"; the evaluator created once (`_state ??=`); `BuildFlows()` empty (stage 3 fills it); `BuildChecklists()` from Task 2; the auto manager empty (as `Md11FoAutoManager`).
- `Bbs757FoStateEvaluator : LVarStateEvaluator`: `OnRequestPollFields` lists every map-control key the checklists read; `TryGetSyntheticValue` gives `FO_GEAR_DOWN`, `FO_GEAR_UP` (lever `B757_GEAR_HANDLE_POS` from plan 02's Gear Lever control and the three stock leg rows plan 06's readouts already register), `FO_FUEL_CONTROLS_RUN`, `FO_FUEL_CONTROLS_CUTOFF` (by variant), `FO_DOORS_CLOSED` (plan 06's door rows all ≤ 5), `FO_IRS_ALL_NAV` (the three IRS switches at 2); NaN until every input is read.
- `Bbs757FoActionExecutor : IFoActionExecutor`: stage 1 `IsAvailable` true when connected, `ExecuteStepAsync` returns false, `WaitForDispatchDrainAsync` completed.
- `Bbs757FoFlightPhaseMonitor`: stage 1 reminders only: transition altitude and level reminders (`TransitionCrossingDetector`, `TransitionAltimeters.ReminderInstead(crossing, false)`, `NoTransitionLoadedReminder`), 10,000 ft lights reminder; it writes nothing.

- [ ] **Steps 1-5** (tests mirroring `MD11/Md11FoProfileTests.cs`: title, evaluator created once, executor unavailable without SimConnect, lists build; `Synthetic_fields_are_nan_until_read`, `Gear_down_needs_lever_and_legs`, `Fuel_controls_by_variant`); commit `feat(b757): First Officer profile, evaluator and reminders`.

### Task 2 [stage 1]: The Normal Checklist

**Files:** Create `FirstOfficer/BBS757/Bbs757FoChecklistDefinitions.cs`; test `tests/.../FirstOfficer/BBS757/Bbs757FoChecklistStructureTests.cs`.

- One group per BlueBird Normal Checklist section, in its order and words (the checklist table in the manuals research, NC 24-84), using the MD-11's builders (`Read`, `Reminder`, `AutoLatch` where FOB-1 says): BEFORE START, BEFORE TAXI, BEFORE TAKEOFF, AFTER TAKEOFF, DESCENT, APPROACH, LANDING, AFTER LANDING, SHUTDOWN, SECURE (and PREFLIGHT if BlueBird's has one). A line becomes a `Read` item when a BlueBird variable shows it (examples: Parking brake SET → the map's Parking Brake row = 1; Seat belt signs ON → `B757_OVHD_SEATBELTS_SWITCH` = 2; Beacon ON → the anti-collision red light switch; Autobrakes RTO → `BBS_AUTOBRAKE_KNOB` = 0; Speedbrake ARMED → `L:B757_FC_SPOILER_LEVER` = 10, DOWN → < 5; Flaps UP → `FLAPS HANDLE INDEX` 0; Gear DOWN / UP and OFF → the synthetic fields; Doors CLOSED → `FO_DOORS_CLOSED`; Fuel control switches CUTOFF → `FO_FUEL_CONTROLS_CUTOFF`; IRS NAV → `FO_IRS_ALL_NAV`; Emergency lights → plan 02's Emergency Lights row); a line that needs a pilot's judgment or a figure (fuel on board, V speeds, briefing, altimeters set) is a `Reminder` with the manual's words. `BEFORE_START_CL` ends with `BSC_ACARS` "Start ACARS"; include an `AFTER_TAKEOFF_CL` group (FO-22's test needs an `AFTER_T` id).
- [ ] **Steps 1-5** (tests mirroring `MD11/Md11FoChecklistStructureTests.cs`: no `*_CL` item with an action (FO-3), unique ids and labels per group, every field synthetic or registered in `BBS757Definition`, every OnRequest input polled); commit `feat(b757): First Officer reads BlueBird's Normal Checklist`.

### Task 3 [stage 1]: Register the 757 First Officer

**Files:** `Forms/FirstOfficer/FirstOfficerWindows.cs` (the 757 row), `.claude/rules/b757.md` (`paths:` only), tests `FirstOfficerWindowsTests.cs` (`["BBS_757"] = "BlueBird 757 First Officer"`), `FoDepartureGroupsTests.cs` (add "BlueBird 757" to its profiles and group ids), `BeforeStartAcarsReadbackTests.cs` (`{ "BlueBird 757", "BSC_ACARS" }`).
- [ ] **Steps 1-5** (the three cross-profile tests fail first, then pass; `ClaudeContextBudgetTests` passes with the new globs); commit `feat(b757): Shift+C opens the 757 First Officer`.
- [ ] **Owner-in-sim:** Shift+C at the gate after a Before start preset: the BEFORE START lines that the cockpit already satisfies are ticked; changing the seat belt switch ticks and unticks its line.

### Task 4 [stage 2]: A silent write path for the First Officer

**Files:** Create `Aircraft/BBS757Definition.FirstOfficer.cs`; test `tests/.../FirstOfficer/BBS757/Bbs757FoWriteTests.cs`.

**Interfaces:** `internal Task<bool> FoSetAsync(string node, double target, CancellationToken ct)` and `internal Task<bool> FoPressAsync(string node, CancellationToken ct)`: reuse `Bbs757Write.ToPosition` / `PressFor` and `WriteIoFor(sim)` (refused when `CanWrite` is false), call `OnOwnWrite(node)` (plan 01), write, read back after the definition's read-back delay, return true only when the control reads the target; speak nothing. Guarded controls go through plan 03's planner (`Bbs757Guarded.Plan`).
- [ ] **Steps 1-5** (tests with a fake `WriteIo`: a set that lands returns true; one that does not returns false; refused when nothing can land; silent); commit `feat(b757): a silent, awaitable write path for the First Officer`.

### Task 5 [stage 2]: Action groups

**Files:** `FirstOfficer/BBS757/Bbs757FoActionExecutor.cs`, `Bbs757FoChecklistDefinitions.cs` (action groups beside the read-back `*_CL` groups), tests `Bbs757FoExecutorTests.cs`, and the cross-profile tests that cover actions (`FoCutGuardSweepTests.cs` a 757 profile like `Md11()`; `FoEngineAndGroundGuardTests.cs`; `EngineStartSelectorLatchTests.cs`).
- Action groups following the Flight Guide's flows (FG 1379-2612): POWER_UP (battery, standby power, APU start), PREFLIGHT (overhead flow: yaw dampers, hydraulics, emergency lights ARMED, ignition, fuel pumps, window heat, pressurization AUTO, packs, bleeds), BEFORE_START (hydraulic electric pumps R, C, L; fuel pumps; seat belts; beacon), AFTER_START, BEFORE_TAKEOFF (lights), AFTER_LANDING, SHUTDOWN, SECURE. Each step's `CheckAction` calls `FoSetAsync` / `FoPressAsync` on the map's node; the engine start selector `AutoLatch`es on N2 (FOB-1); cut guards and `LeaveAloneWhen` on fuel, IRS, battery, hydraulics (FO-25); a refused write raises the action-failed speech (FO-9).
- [ ] **Steps 1-5**; commit `feat(b757): First Officer action groups`.

### Task 6 [stage 3]: Flows and the phase monitor

**Files:** `FirstOfficer/BBS757/Bbs757FoFlowDefinitions.cs` (new), `Bbs757FoProfile.cs` (`BuildFlows`), `Bbs757FoFlightPhaseMonitor.cs`; tests `Bbs757FoFlowTests.cs` and the flow cross-profile tests (`FlowStepRunOnlyIfSentIdTests.cs`, `FlowStepDependencyIdTests.cs`, `FoChecklistRefinementTests.cs` with a `B757_BeforeStartTail`).
- Flows built from the action groups (each step completing its checklist item, FO-6, FO-7), with progress speech (FO-26); the phase monitor gains the writes the other First Officers make when their settings allow (landing lights at 10,000 ft, altimeters to standard at the transition altitude through `KOHLSMAN_SET` as Ctrl+B does), each through `FoSetAsync` or the definition's altimeter set.
- [ ] **Steps 1-5**; commit `feat(b757): First Officer flows and phase automation`.

## In-sim checks (owner)

1. Stage 1: Task 3's check; at shutdown, the SHUTDOWN lines tick as the switches move.
2. Stage 2: from cold and dark, run POWER_UP and PREFLIGHT from the window; each step's switch moves and its line ticks; a refused step says so.
3. Stage 3: the Before start flow from start to "Start ACARS"; climbing through 10,000 ft with lights automation on.

## Handoff to plan 16

- **Pilot-facing:** "The BlueBird 757 has a First Officer (Shift+C): BlueBird's Normal Checklist, ticking itself from the cockpit; the First Officer can carry out the power-up, preflight, before-start, after-landing, shutdown and secure actions and run their flows; transition and 10,000 ft reminders or automation as the First Officer settings say." Guide: the First Officer chapter (the 757 row and what it reads) and the 757 section. Changelog `feature`.
- **docs/first-officer.md:** the 757 in its aircraft list. **docs/b757.md:** "Not here yet" loses the First Officer.

## Execution record

Kept in this file only, so no other plan's push ever touches it. Claim the plan by filling in **Claimed by** and
setting **Status** to claimed, and push that one change before starting; if the push stops on a conflict in these
lines, someone claimed it first. Fill in the rest as you go and when the plan lands (index, "Records").

- **Status:** not started
- **Claimed by:** none
- **Worked by:** (people; add "with subagents" if run through superpowers:subagent-driven-development)
- **Started:** (date; the `B757` commit you branched from)
- **Landed:** (date; the last commit of this plan on `B757`)
- **Tests at landing:** (full suite passed / failed, after the last rebase)
- **Tasks:** (done; left, and why)
- **Waiting on the owner:** ([owner] and [in flight] tasks and in-sim checks not run yet)
- **In-sim results:** (each check: passed or failed, date; or "none run")
- **Changed from the plan:** (a name, a value or a task changed, dropped or added, and why)
- **Rulings made while working:** (question, answer, who decided)
