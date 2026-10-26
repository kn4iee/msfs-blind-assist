# BlueBird 757 Panel Writes Implementation Plan (plan 03, early: fixes and essentials)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** Fixes first: the emergency lights and the stab trim cutouts can be set from the panels (the app opens and closes their guards), and a fire handle unlocked from the app locks again. Then the essential: the pressurization landing altitude can be typed. (The trims and other spring-loaded knobs are plan 12, which takes these files over.)

**Architecture:** A pure planner (`Bbs757Guarded`) turns a pick on a guarded control into the calls that open its guard, write its state var and, for the position BlueBird sets on closing, close the guard; `BBS757Definition.Interaction.cs` routes a guarded control to it. The landing altitude is a number field written as measured (target − 10 ft, then one knob step) and read back.

**Tech Stack:** C# 13 / .NET 10 Windows Forms, xUnit.

**Spec:** `docs/b757/spec-gaps.md` sections 1 (#6, #7), 3.3, 3.7, 4 (R63, R64, R68); plan 01's seams; plan 02's guarded rows.

## Global Constraints

- Owned files (`Overhead.cs`, `Bbs757Write.cs` and `Bbs757Rpn.cs` then passed to plan 12): `Aircraft/BBS757Definition.Interaction.cs` (after plan 01), `Aircraft/BBS757Definition.Overhead.cs` (new), `Aircraft/BBS757/Bbs757Guarded.cs` (new), `Aircraft/BBS757/Bbs757LandingAltitude.cs` (new), `Aircraft/BBS757/Bbs757Write.cs`, `Aircraft/BBS757/Bbs757Rpn.cs`, tests `Bbs757WriteTests.cs`, `Bbs757RpnTests.cs`, `Bbs757GuardedTests.cs` (new), `Bbs757LandingAltitudeTests.cs` (new), `docs/b757/evidence/flight-03.md`.
- Guard semantics (BlueBird, spec 3.3): a guarded control moves only with its guard open (guard value ≠ `GuardClosed`); closing the guard writes `CloseSets` to the control (emergency lights 1 ARMED, stab trim cutouts 0 NORMAL). After closing, the app also writes `CloseSets` itself: if BlueBird's close already wrote it, the write changes nothing; if a guard's click does not, the control still lands.
- Every write path calls plan 01's `OnOwnWrite(node)` before its first write, as `PressAsync` and `SetPositionAsync` do.
- Landing altitude: `L:B757_OVHD_LDG_ELEV` feet, `L:B757_OVHD_LDG_ELEV_CHG` +1 = +10 ft (measured). Range -1070 to 14100 ft, multiples of 10.
- Speech (CORE-7): a combo pick is read by the screen reader, so a guarded pick that lands says nothing; one that does not land says so once. The typed landing altitude always speaks its read-back.
- Variables added: 1.

## Review Focus

- A guarded control whose value or guard state cannot be read writes nothing and says "{label} unavailable" (Task 1 test).
- Two quick picks on the emergency lights within the pick settle: only the last is sent (Task 2, the pick-supersede machinery).
- A landing altitude typed under de-DE ("1.030", "1030,0") is the pilot's 1030 ft and goes out invariant (Task 5 test).
- A landing altitude that does not take (BBS Nest stopped) speaks the value read back (Task 6).

---

### Task 0 [controller]: Claim this plan

**Who:** whoever runs the plan, before any other task; with subagents, the controller itself, never an implementer. A claim counts only once it is on the remote `B757`: one that exists only on your machine claims nothing, and if someone else's claim reaches `B757` first, the plan is theirs.

- [ ] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/B757 -- docs/b757/plans/02-gaps/03-panel-writes.md`. Anything but "not started" and "none" means the plan is taken: stop.
- [ ] **Step 2: Branch and claim.** Branch as in the index's workflow step 1. In this file's "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the commit you branched from. Commit only that change: `docs(b757): claim plan 03`, with the `Plan: 03` trailer.
- [ ] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/B757`, `git push <remote> HEAD:B757`. If the rebase stops on these lines, someone claimed the plan first: `git rebase --abort` and stop. If the push is rejected, repeat this step. Task 1 starts only once the push has gone through.

### Task 1: The guarded-write planner

**Files:** Create `MSFSBlindAssist/Aircraft/BBS757/Bbs757Guarded.cs`; test `tests/MSFSBlindAssist.Tests/Bbs757GuardedTests.cs`.

**Interfaces:**
- Consumes `Bbs757Control.Guard`, `GuardClosed`, `CloseSets` (plan 01); `Bbs757Rpn.ToggleTo(Bbs757Control c, double current, double target)`, `Bbs757Rpn.SetLVar(string lvar, double value)`.
- Produces:
  ```csharp
  public sealed record Bbs757GuardedPlan(bool Refused, IReadOnlyList<string> Calls);
  public static class Bbs757Guarded
  {
      public const int StepGapMs = 300;
      public static Bbs757GuardedPlan Plan(Bbs757Control control, Bbs757Control guard,
          double? current, double? guardState, double target);
  }
  ```
- Rules: `current` or `guardState` null, or no `StateVar` → `Refused`. `target == CloseSets`: guard open → [guard toggled to `GuardClosed`, then `SetLVar(StateVar, target)`]; guard closed and current == target → []; guard closed and current ≠ target → [`SetLVar`]. `target != CloseSets`: guard closed → [guard toggled open, `SetLVar(StateVar, target)`]; guard open → current == target ? [] : [`SetLVar`].

- [ ] **Step 1: Write the failing tests** with in-memory controls (emergency lights 0 OFF / 1 ARMED / 2 ON, guard closed 0, close sets 1; stab trim C 0 NORMAL / 1 CUTOUT, guard closed 0, close sets 0): `Off_from_armed_opens_the_guard_and_writes_zero`; `Armed_from_off_closes_the_guard_and_writes_one`; `On_with_the_guard_open_writes_two`; `Already_there_sends_nothing`; `Cutout_opens_and_writes_one`; `Normal_closes_the_guard_and_writes_zero`; `Unread_state_refuses`; `Calls_are_invariant_under_de_DE`.
- [ ] **Step 2: Run** `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter Bbs757GuardedTests`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the full suite.** Expected: PASS.
- [ ] **Step 5: Commit** `fix(b757): plan a guarded control's write`.

### Task 2: Route guarded picks through it

**Files:** Create `MSFSBlindAssist/Aircraft/BBS757Definition.Overhead.cs`; modify `BBS757Definition.Interaction.cs` (`HandleUIVariableSet`, before the momentary branch); test `Bbs757GuardedTests.cs`.

**Interfaces:**
- `private Task<bool> SetGuardedAsync(Bbs757Control control, Bbs757Control guard, double target, Bbs757SimIo io, ScreenReaderAnnouncer announcer)`, following `SetPositionAsync` (pick settle and supersede through `_pendingPicks`, refusal through `io.CanWrite()`, `OnOwnWrite(control.Node)`): read both fresh, `Bbs757Guarded.Plan`, send `StepGapMs` apart, wait `ReadBackDelayMs`, request both rows' re-reads; the control not at the target → "{label} did not move".
- `HandleUIVariableSet`: a control whose `Guard` names a control in `_byNode` goes to `SetGuardedAsync`; a `Guard` naming none logs a warning and says "{label} unavailable".

- [ ] **Step 1: Write the failing tests** with a fake `WriteIo`: `Guarded_pick_sends_the_plan` (pick 0, reads 1 and 0 → the guard's press, then `0 (>L:B757_OVHD_PASS_OXY_Toggle_Switch_CLICK)`); `Guarded_pick_that_does_not_land_says_so`.
- [ ] **Steps 2-4:** run (FAIL), implement, full suite (PASS).
- [ ] **Step 5: Commit** `fix(b757): guarded controls open and close their guards`.

### Task 3: Fire handles lock again after an app unlock

**Files:** `BBS757Definition.Overhead.cs`, `Aircraft/BBS757/Bbs757Write.cs`; test `Bbs757WriteTests.cs`.

**Interfaces:**
- Read the fire handle unlock components in the cockpit XML (around line 15535): which L:var holds "unlocked", and the relock time the component's `O:` timer counts (the calculator cannot set `O:` vars, so the cockpit's relock never runs after an app press).
- `public static string? RelockAfterUnlock(Bbs757Control c, out int delayMs)` in `Bbs757Write`: for the three unlock buttons, the write that relocks and the vendor's delay; null for any other control.
- After an unlock press (`PressAsync` path), schedule that write after `delayMs` on the definition's lifetime (canceled on switch-away is fine: the handle then stays as the cockpit would leave it).

- [ ] **Step 1: Write the failing tests:** `Unlock_buttons_relock` (each of the three yields its relock write and delay); `Other_buttons_do_not`.
- [ ] **Steps 2-4:** run, implement, full suite.
- [ ] **Step 5: Commit** `fix(b757): a fire handle unlocked from the app locks again`.

### Task 4 [owner]: In-sim check of the fixes

- [ ] On the ground, APU running: Emergency Lights OFF (guard opens), ARMED (guard closes), ON, ARMED; Stab Trim C Cutout CUTOUT then NORMAL (ground only); a fire handle unlock, wait past the relock time, the handle reads locked. Record anything unexpected in `docs/b757/evidence/flight-03.md`.

### Task 5: Landing altitude, pure

**Files:** Create `MSFSBlindAssist/Aircraft/BBS757/Bbs757LandingAltitude.cs`; test `Bbs757LandingAltitudeTests.cs`.

**Interfaces:**
  ```csharp
  public static class Bbs757LandingAltitude
  {
      public const string ValueVar = "B757_OVHD_LDG_ELEV", StepVar = "B757_OVHD_LDG_ELEV_CHG";
      public const string SetKey = "BBS757_LDG_ALT_SET", DisplayKey = "BBS757_LDG_ALT";
      public const int MinFeet = -1070, MaxFeet = 14100;
      public static bool TryParseText(string? text, out int feet, out string error);
      public static string Write(int feet);      // "{feet - 10} (>L:B757_OVHD_LDG_ELEV) 1 (>L:B757_OVHD_LDG_ELEV_CHG)"
      public static string Phrase(double? feet); // "Landing altitude 1030 feet" / "… minus 60 feet" / "Landing altitude unavailable"
      public static string Display(double feet); // "1030 feet"
  }
  ```
  Errors (exact): empty → "Type the landing altitude in feet first."; otherwise → "Landing altitude must be from -1070 to 14100 feet." Rounds to the nearest 10 (`NumberParsing.TryParseTyped`).

- [ ] **Steps 1-5** (tests `Parses_and_rounds`, `Refuses_empty_and_out_of_range`, `De_DE_typing`, `Phrases`); commit `feat(b757): landing altitude entry, pure`.

### Task 6: The landing altitude field on the Pressurization panel

**Files:** `BBS757Definition.Overhead.cs` (implements plan 01's `AddOverheadVariables`, `AddOverheadPanels`, `HandleOverheadTypedText`, `FormatOverheadDisplay`); test `Bbs757LandingAltitudeTests.cs`.

**Interfaces:**
- `SetKey`: the squawk field's pattern (`BBS757Definition.Readouts.cs`: a key containing `_SET`, an Event registration whose event is never sent, `UnparseableTextAsNaN = true`), label "Landing Altitude"; `DisplayKey`: LVar `B757_OVHD_LDG_ELEV`, OnRequest, read-only status row "Landing Altitude", `ExcludeFromMonitorManager`, shown through `FormatOverheadDisplay` as `Display(value)`.
- Panels: the field first in "Pressurization", the row in its status display.
- `HandleOverheadTypedText` for `SetKey`: parse (refusal → the error); `CanWrite` false → "Landing altitude unavailable"; write `Write(feet)`; after `ReadBackDelayMs` read `DisplayKey` fresh; speak `Phrase`.

- [ ] **Steps 1-5** (tests `Pressurization_panel_starts_with_the_field`, `Typed_altitude_writes_and_reads_back`, `Typed_altitude_speaks_what_came_back`, `Display_says_feet`); commit `feat(b757): type the landing altitude on the Pressurization panel`.

## In-sim checks (owner)

1. The fixes: Task 4.
2. The essential: Pressurization, type 1030 → "Landing altitude 1030 feet"; type 645 → "Landing altitude 650 feet".

## Handoff to plan 16

- **Pilot-facing:** the fixes "757: the emergency lights can be armed, switched off or on from the Overhead miscellaneous panel and the stab trim cutouts set from the Stabilizer trim panel (the app opens and closes their guards); a fire handle unlocked from the app locks again" (`fix`); the essential "type the destination's landing altitude on the Pressurization panel (the 757 has no cruise altitude selector: the FMC's cruise level is on the CDU's PERF INIT page)" (`feature`).
- **Proposed rule line:** `- [B757-7] Set the 757 landing altitude as `Bbs757LandingAltitude.Write` does: `L:B757_OVHD_LDG_ELEV` = target − 10, then one `L:B757_OVHD_LDG_ELEV_CHG` step; a direct write alone holds but leaves the panel's digits stale until the next knob step (measured). Full: docs/invariants/b757.md#b757-7`

## Execution record

Kept in this file only, so no other plan's push ever touches it. Task 0 claims the plan (**Status**, **Claimed by**,
**Started**); fill in the rest as you go and when the plan lands (index, "Records").

- **Status:** claimed
- **Claimed by:** kn4iee (the owner), 2026-10-26
- **Worked by:** (people; add "with subagents" if run through superpowers:subagent-driven-development)
- **Started:** 2026-10-26, from `c8a80bd6f`
- **Landed:** (date; the last commit of this plan on `B757`)
- **Tests at landing:** (full suite passed / failed, after the last rebase)
- **Tasks:** (done; left, and why)
- **Waiting on the owner:** ([owner] and [in flight] tasks and in-sim checks not run yet)
- **In-sim results:** (each check: passed or failed, date; or "none run")
- **Changed from the plan:** (a name, a value or a task changed, dropped or added, and why)
- **Rulings made while working:** (question, answer, who decided)
