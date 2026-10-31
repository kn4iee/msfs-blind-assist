# A350 Altimeter Window Implementation Plan (plan 02, early: fix)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** A QNH typed in the A350's Altimeter window (Ctrl+B) sets every target or none: all three are checked before anything is sent, the entry follows the OIS's LINKED INSTR option the way one knob turned in the cockpit does, and no side leaves STD unless the pilot pulls it.

**Architecture:** Today the window calls `PressRow` for the captain's side, the first officer's and the ISIS, each planned and sent alone (`A350TypedEntry.Plan`). A new pure planner, `A350BaroEntry`, plans the three together from one read, refuses the whole entry in one sentence when any part would refuse, and with LINKED INSTR on plans the captain's side only (the aircraft's `LinkBaros` copies it). The definition runs the plan with the existing typed-value runner and reads every target back, saying a side that left STD.

**Tech Stack:** C# 13 / .NET 10 Windows Forms, xUnit; `tools/A350Probe` for the measurements.

**Spec:** `docs/a350/spec.md` sections 2.1, 4 and 5 (questions 1 and 2); `docs/a350/evidence/flight-2026-10-31.md` "The Altimeter window"; `docs/inibuilds-a350.md` "Typed values".

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply.
- Owned files: `Aircraft/A350/A350TypedEntry.cs`, `A350FcuCallouts.cs`, `A350FcuWindows.cs`, `A350Readouts.cs`, `A350BaroEntry.cs` (new), `Aircraft/IniBuildsA350Definition.Interaction.cs`, `Forms/A350/A350BaroWindow.cs`, `A350FcuValueWindow.cs`, `A350FcuWindowBase.cs`, `A350AutopilotWindow.cs`; tests `A350TypedEntryTests.cs`, `A350FcuWindowsTests.cs`, `A350FcuCalloutsTests.cs`, `A350WindowWritesTests.cs`, `A350BaroEntryTests.cs` (new); `docs/inibuilds-a350.md` "Typed values"; `docs/a350/evidence/02-altimeter.md` (new).
- Measured on 2026-10-31 (1.2.8, in flight): the ISIS short write `16510 3 r (>K:2:KOHLSMAN_SET)` and its knob click took both sides from STD to QNH 1013 and left the ISIS at 1013.25; every later knob click, on any of the three, landed on 1013.25; only the direct corrections (`16526 n r (>K:2:KOHLSMAN_SET)`) set 30.50. The captain's unit button switched the first officer's side too.
- The baro event is the sim's own `{mb*16} {side} r (>K:2:KOHLSMAN_SET)` (sides 1, 2, the ISIS 3); STD is `XMLVAR_Baro{side}_Mode` 3; a side's unit is `XMLVAR_Baro_Selector_HPA_{side}` (1 hectopascals); the option is `L:INI_LINKED_INSTRUMENTS`. The STD preset is stepped by clicks 300 ms apart (`A350TypedStepper`), in flight only.
- Owner questions 1 and 2 (spec section 5): build the recommendations (refuse it all in one sentence; linked: the captain's side only) unless the Answer says otherwise.
- Writes in the sim only with the sim owner's go-ahead; restore the owner's baro modes, units and LINKED INSTR afterwards.

## Review Focus

- Both sides on STD in hectopascals, 30.5 typed: one sentence, and nothing at all is sent, the ISIS included (Task 2 test `Inches_typed_on_two_hectopascal_sides_refuses_it_all_and_sends_nothing`).
- The sides in different units: the entry is refused whatever the value fits, naming each side's unit (Task 2 test).
- LINKED INSTR unreadable: never assume it off or on; the entry says the option cannot be read and sends nothing (Task 2 test).
- A side that leaves STD after an entry that did not pull it is said, once, with what it now shows (Task 3 test).
- The first officer's and the ISIS's background call-outs say nothing for a change the entry made and confirmed (Task 3 test with `A350FcuCallouts`).

---

### Task 0 [controller]: Claim this plan

**Who:** whoever runs the plan, before any other task; with subagents, the controller itself, never an implementer. A claim counts only once it is on the remote branch: one that exists only on your machine claims nothing, and if someone else's claim reaches the branch first, the plan is theirs.

- [ ] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/feat/inibuilds-a350 -- docs/a350/plans/02-altimeter.md`. Anything but "not started" and "none" means the plan is taken: stop.
- [ ] **Step 2: Branch and claim.** Branch as in the index's workflow step 1. In this file's "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the commit you branched from. Commit only that change: `docs(a350): claim plan 02`, with the `Plan: 02` trailer.
- [ ] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/feat/inibuilds-a350`, `git push <remote> HEAD:feat/inibuilds-a350`. If the rebase stops on these lines, someone claimed the plan first: `git rebase --abort` and stop. If the push is rejected, repeat this step. Task 1 starts only once the push has gone through.

### Task 1 [in flight] [owner]: Measure what each baro write does, linked and not

**Files:** create `docs/a350/evidence/02-altimeter.md`.

The owner puts the aircraft in a stable cruise or hold above the transition altitude (an OIS save restored in the air, AP and A/THR re-engaged), both sides on STD in hectopascals, and gives the go-ahead; Claude drives `A350Probe repl` (`rpn`, `lv`, `row`) and reads `XMLVAR_Baro1_Mode`, `XMLVAR_Baro2_Mode`, `XMLVAR_Baro_Selector_HPA_1`/`_2`, `KOHLSMAN SETTING MB:1`/`2`/`3`, `INI_BARO_PRESET_CPT`/`_FO` with `_SHOW`, and `INI_LINKED_INSTRUMENTS` after each step (2 s).

- [ ] **Step 1:** Read the option's state as the OIS shows it (OPTIONS > AIRCRAFT > LINKED INSTR, through MSFSBA's OIS window) beside `INI_LINKED_INSTRUMENTS`.
- [ ] **Step 2: With LINKED INSTR on**, one step at a time, reading all after each: the ISIS's short write alone; the ISIS's knob click alone; the captain's knob click (does the first officer's preset show and follow?); the captain's pull (does the first officer's side pull too, and the ISIS?); then, on QNH, a captain short write and click (where does it land?), and a direct `KOHLSMAN_SET` 1 alone (does it hold, do the first officer and the ISIS follow, and does the captain's PFD draw it: `A350Probe draw live EFIS#0`?); the captain's unit button (does the first officer's follow?).
- [ ] **Step 3: With LINKED INSTR off**, the same steps.
- [ ] **Step 4:** Restore the owner's modes, units and option; write the results as a table (step, what each variable did) and what they decide: which writes move other targets, and whether a knob click lands one step past a short write on 1.2.8. If they contradict Task 2's rules below, stop and put the difference to the owner before Task 2.

### Task 2: The entry planned as one

**Files:** create `Aircraft/A350/A350BaroEntry.cs`; modify `A350TypedEntry.cs` (its `Inputs` add `INI_LINKED_INSTRUMENTS` to the three baro fields, so `BuildVariables` streams it); test `A350BaroEntryTests.cs`.

**Interfaces:**
- Consumes `A350TypedEntry.Plan`, `CaptainQnh`, `FirstOfficerQnh`, `IsisQnh`, `A350WritePlan.UnknownPositionRefusal`.
- Produces:
  ```csharp
  internal sealed record A350BaroTarget(A350TypedField Field, A350TypedPlan Plan);
  internal sealed record A350BaroEntryPlan(string? Refusal, IReadOnlyList<A350BaroTarget> Targets, bool Linked,
      IReadOnlyList<int> SidesOnStd);
  internal static class A350BaroEntry
  {
      public const string LinkedVar = "INI_LINKED_INSTRUMENTS";
      public static A350BaroEntryPlan Plan(double typed, Func<string, double?> read);
  }
  ```
- Rules (exact copy):
  - Any input of the three fields or `LinkedVar` unread: refused with `A350WritePlan.UnknownPositionRefusal`, said once.
  - Both sides in one unit and either side's plan refused: that side's refusal sentence, once ("QNH must be between 900 and 1100 hectopascals." / "QNH must be between 26.6 and 32.5 inches.").
  - The sides in different units: refused, whatever the value fits: "QNH not set: the captain's baro shows {unit}, the first officer's {unit}. Set both sides to one unit first." with "hectopascals" or "inches".
  - Linked (`LinkedVar` ≥ 0.5): one target, the captain's (its preset on STD, its value on QNH); not linked: the captain's, the first officer's, then the ISIS's. Nothing is planned for the ISIS while a side is on STD, linked or not, unless Task 1 showed its write leaves the sides alone.
  - `SidesOnStd`: the sides read on STD (1, 2), for Task 3's check.
- [ ] **Steps 1-5** (tests `Inches_typed_on_two_hectopascal_sides_refuses_it_all_and_sends_nothing`, `Sides_in_different_units_refuse_naming_each_unit`, `An_unread_linked_option_refuses`, `Linked_plans_the_captains_side_only`, `Linked_on_std_plans_the_captains_preset`, `Unlinked_plans_three_targets_in_order`, `No_isis_target_while_a_side_is_on_std`, `The_refusal_reads_the_same_under_de_DE_and_sv_SE`); commit `fix(a350): the Altimeter window plans its three baros as one entry`.

### Task 3: The window runs the entry and reads every target back

**Files:** `Aircraft/IniBuildsA350Definition.Interaction.cs`, `Forms/A350/A350BaroWindow.cs`, `Aircraft/A350/A350FcuCallouts.cs` (only if its background baro call-out needs the entry's echo); tests `A350BaroEntryTests.cs`, `A350FcuCalloutsTests.cs`.

**Interfaces:**
- `internal void PressBaroEntry(double typed, SimConnectManager sim, ScreenReaderAnnouncer announcer)` in `IniBuildsA350Definition.Interaction.cs`: plans with `A350BaroEntry.Plan` on the cached reads `HandleTyped` uses; a refusal is announced once and nothing is sent; `CanLand` false says "Altimeter unavailable"; else each target runs through `TypedAsync` in order, one after the other finishes, and every baro row the entry can move (the copies too, when linked) has its typed echo armed first (`ArmTypedEcho`), so a confirmed change is said once, by its target.
- After the last target: each side in `SidesOnStd` that no longer reads STD, without a pull asked, is said once: "{Captain|First officer} altimeter left STD, now {value} {hectopascals|inches}" (the value as `A350FcuCallouts` words it).
- `A350BaroWindow`: Enter calls `aircraft.PressBaroEntry(v, simConnect, announcer)` once, in place of the three `PressRow` calls; its description says "Enter sets both sides and the ISIS, or the captain's side when LINKED INSTR copies it".
- [ ] **Steps 1-5** (tests `A_side_left_std_unasked_is_said_once`, `A_linked_copy_is_not_said_twice`, `Enter_runs_one_entry`, the last with the window's Enter handler on a fake definition); commit `fix(a350): the Altimeter window sets its baros as one entry and says a side that left STD`.

### Task 4 [sim]: FCU changes from external hardware are announced

**Files:** `docs/a350/evidence/02-altimeter.md`; a fix in `A350FcuCallouts.cs` or `A350FcuWindows.cs` if one is needed (test in `A350FcuCalloutsTests.cs`).

- [ ] **Step 1:** With MSFSBA running and the owner's go-ahead, turn each FCU knob from outside the app (the sim's own events through `A350Probe rpn`, as hardware sends them: `AIRLINER_FCU_SPD_KNOB`, `_HDG_KNOB`, `_ALT_KNOB`, `_VS_KNOB`, both baro knobs): each change is said once, in its window's words, after it settles, and nothing is said for MSFSBA's own typed values.
- [ ] **Step 2:** Record each knob's result; fix a miss test-first (Steps 1-5, test named for the knob); commit `fix(a350): <knob> turned from hardware is said`.

### Task 5 [in flight] [owner]: The fix in the cockpit

- [ ] **Step 1:** In flight above the transition altitude, LINKED INSTR as the owner flies it, both sides on STD in hectopascals: type 30.5 → one sentence, both sides stay STD, the ISIS unchanged.
- [ ] **Step 2:** Type 1033 → "Captain altimeter preset 1033 hectopascals" (and the first officer's preset as Task 1 found), both sides still STD; pull both (the window's QNH button) → QNH 1033 on both, and the ISIS as Task 1 found.
- [ ] **Step 3:** The same with LINKED INSTR off; then on the ground, QNH typed sets all three.
- [ ] **Step 4:** Record the speech (`a350_speech.log`) and each step's reads in the evidence file.

## Handoff to plan 21

- **Pilot-facing:** "A350 Altimeter window: a typed QNH is checked for both sides and the ISIS before anything is set, follows the OIS's LINKED INSTR option, and no longer takes a side off STD." Guide: the A350's Altimeter window; changelog `fix`.
- **Rule proposal:** a new A350 rule: the Altimeter window plans its targets as one entry from one read (`A350BaroEntry.Plan`) and never clicks the ISIS while a side is on STD; full text: Task 1's measurements.
- **For plan 06:** what LINKED INSTR does to the baros (Task 1), for its option's ledger line.

## Execution record

Kept in this file only, so no other plan's push ever touches it. Task 0 claims the plan (**Status**, **Claimed by**,
**Started**); fill in the rest as you go and when the plan lands (index, "Records").

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
