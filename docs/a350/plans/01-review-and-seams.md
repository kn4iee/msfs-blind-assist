# A350 Review and Seams Implementation Plan (plan 01, first and alone)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** The commits nobody independent has reviewed (`345bfd07f` to `7e6d5fbd1`) are reviewed and their real findings fixed test-first, and the three seams later plans add to exist, so plans 02-11 start in parallel from reviewed code without editing one line twice.

**Architecture:** First a review that gets the code, not anyone's conclusions; then one fix commit per Critical or Important finding, each with its failing test. Then three seams: Ctrl+M rows for new display call-outs, listed by area in a partial class whose parts the area plans own; the E/WD monitor muted by key, so plan 04 can add rows to it; and a hook for each good captain EFIS draw, so plan 08 can read the ND in the background without editing plan 07's monitor.

**Tech Stack:** C# 13 / .NET 10 Windows Forms, xUnit.

**Spec:** `docs/a350/spec.md` sections 3 (handoff 3.3) and 4; `docs/a350/plans/00-index.md` "File ownership".

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply.
- The review range is the branch's own commits: `git log --first-parent --no-merges e19171d2c..7e6d5fbd1` (12 commits: `345bfd07f`, `01fe0fffb`, `c62d1a5e6`, `847bb11eb`, `1a42974ca`, `ccd29e1b9`, `a9226acf2`, `4a31320cc`, `1ea79f697`, `3b2520828`, `3fd7bf50d`, `7e6d5fbd1`); the merge `ecb6bc21d` brings `main`'s own reviewed work and is not in it.
- While 01 runs it owns every file those commits touch (`git log --first-parent --no-merges --name-only e19171d2c..7e6d5fbd1`), plus the seams' files below. Nobody else works on the branch's code until 01 records its base commit.
- A Minor finding is not fixed here: it goes in the execution record with the plan owning its file (the index's table).
- The seams add no behavior: the Ctrl+M dialog lists the same rows in the same order, nothing new is said.

## Review Focus

- A review that only re-reads the commit messages finds nothing: the reviewer gets the diff and the files around it, never this plan's guesses (Task 1).
- Output N (`a9226acf2`, `Services/NavRadioFormat.cs`) formats numbers: a comma-decimal culture must not change what is said (Task 1 checks a de-DE test exists; Task 2 adds it if not).
- `NavRadioFormat` and the `MainForm.Announcers.cs`/`MainForm.Hotkeys.cs` changes are shared code: every aircraft that reaches them is checked, not only the A350 (Task 1).
- An added Ctrl+M row with a key a variable row already has would list one setting twice (Task 3 test).
- The EFIS hook runs on the monitor's pool thread: what it calls must never touch SimConnect (SIM-5, SIM-20) (Task 5 test and doc comment).

---

### Task 0 [controller]: Claim this plan

**Who:** whoever runs the plan, before any other task; with subagents, the controller itself, never an implementer. A claim counts only once it is on the remote branch: one that exists only on your machine claims nothing, and if someone else's claim reaches the branch first, the plan is theirs.

- [ ] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/feat/inibuilds-a350 -- docs/a350/plans/01-review-and-seams.md`. Anything but "not started" and "none" means the plan is taken: stop.
- [ ] **Step 2: Branch and claim.** Branch as in the index's workflow step 1. In this file's "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the commit you branched from. Commit only that change: `docs(a350): claim plan 01`, with the `Plan: 01` trailer.
- [ ] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/feat/inibuilds-a350`, `git push <remote> HEAD:feat/inibuilds-a350`. If the rebase stops on these lines, someone claimed the plan first: `git rebase --abort` and stop. If the push is rejected, repeat this step. Task 1 starts only once the push has gone through.

### Task 1: The review

**Files:** none changed; findings go in this file's execution record.

- [ ] **Step 1: Run an independent correctness review** of the range, with superpowers:requesting-code-review (or the repo's `code-review` skill at high effort) on `git diff e19171d2c 7e6d5fbd1` restricted to the files the first-parent commits touch. Give the reviewer the diff, CLAUDE.md, `.claude/rules/inibuilds-a350.md` and the rule files the touched files load, and these areas to read whole, not the commit messages' claims: the final review's fixes (`1a42974ca`); Output N (`a9226acf2`: `Services/NavRadioFormat.cs`, its callers in `MainForm.Announcers.cs` and `MainForm.Hotkeys.cs`, every aircraft that reaches them); the vertical display's waypoint scales (`a9226acf2`, `A350NdPage`, `A350NdMap`, `A350GroundDisplays`); VERT REV's speed limit row (`1ea79f697`, `A350MfdPage`); one MFD value under two headings (`847bb11eb`); the ND's target names (`ccd29e1b9`); the other aircraft's refusals naming the A350 (`01fe0fffb`); the taxi lead (`c62d1a5e6`); the visual guidance biases (`3b2520828`); the First Officer's `ExtendWithinKts = 3` (`3fd7bf50d`, `A350FOAutoManager`).
- [ ] **Step 2: Check CLAUDE.md's "Check the whole of what you touched"** over the same range: each spoken or formatted change has a de-DE or sv-SE test; each shared change holds on every aircraft using it; each display fix was checked over its whole page.
- [ ] **Step 3: Record every finding** in "Review findings" below (file:line, what fails, the input that shows it, severity Critical / Important / Minor, owning plan), and say which the reviewer verified against the code.

### Task 2: Fix the Critical and Important findings

**Files:** the finding's own; its test in the existing test file of that code (the index's "Tests" list) or a new `A350<Feature>ReviewTests.cs`.

- [ ] **Steps 1-5 per finding:** a failing test that shows the finding, the fix, the suite green, one commit each: `fix(a350): <what the finding was>` with the `Plan: 01` trailer. A finding that needs the sim to show it goes to its owning plan instead, written there as a handoff request in this file's record.

### Task 3: Seam: Ctrl+M rows for new display call-outs, by area

**Files:** Create `Aircraft/A350/A350CalloutRows.cs`, `A350CalloutRows.Ecam.cs`, `A350CalloutRows.Pfd.cs`, `A350CalloutRows.Nd.cs`, `A350CalloutRows.Mfd.cs`; modify `Forms/A350/A350MonitorManagerForm.cs`; test `tests/MSFSBlindAssist.Tests/A350/A350CalloutRowsTests.cs`.

**Interfaces:**
- Produces, for plans 04 (Ecam), 07 (Pfd), 08 (Nd) and 09 (Mfd), each owning its own part's file:
  ```csharp
  namespace MSFSBlindAssist.Aircraft.A350;
  internal static partial class A350CalloutRows
  {
      /// Every row the plans add, in area order; the rows that existed before them stay in A350MonitorManagerForm.BuildRows.
      public static IEnumerable<(string Key, string Label)> All => Ecam.Concat(Pfd).Concat(Nd).Concat(Mfd);
  }
  // A350CalloutRows.Ecam.cs (likewise Pfd, Nd, Mfd), empty until its plan fills it:
  internal static partial class A350CalloutRows
  {
      public static readonly IReadOnlyList<(string Key, string Label)> Ecam = Array.Empty<(string Key, string Label)>();
  }
  ```
- `A350MonitorManagerForm.BuildRows` appends `A350CalloutRows.All` after its last row (`A350MemoryWarning`), each as `new MonitorRow(key, label)`.
- [ ] **Steps 1-5** (tests `Added_rows_follow_the_existing_display_rows`, `No_added_row_shares_a_key_with_a_variable_row_or_another_added_row`, the second built on a definition's real variables and the four lists); commit `refactor(a350): the display call-outs' added Ctrl+M rows are listed by area`.

### Task 4: Seam: the E/WD monitor muted by key

**Files:** `Aircraft/A350/Display/A350EcamMonitor.cs`, `MainForm.AircraftSwitch.cs` (its `StartA350DisplayService` only); tests `A350EcamMonitorTests.cs`.

**Interfaces:**
- `public A350EcamMonitor(Func<string, bool> muted, Action<string> speak, Func<DateTime>? now = null)`; `Observe` asks `_muted(MuteKey)`; plan 04 asks its own keys the same way.
- `StartA350DisplayService` passes `key => Settings.SettingsManager.Current.A350DisabledMonitorVariablesSet.Contains(key)`.
- [ ] **Steps 1-5** (tests `The_ecam_row_mutes_by_its_key`, and the existing tests moved to the keyed constructor); commit `refactor(a350): the E/WD monitor asks its mutes by key`.

### Task 5: Seam: each good captain EFIS draw handed on

**Files:** `Aircraft/A350/Display/A350PfdMonitor.cs`; create `Aircraft/IniBuildsA350Definition.Hooks.cs`; `MainForm.AircraftSwitch.cs` (its `StartA350DisplayService` only); test `tests/MSFSBlindAssist.Tests/A350/A350EfisHookTests.cs`.

**Interfaces:**
- `A350PfdMonitor`: `public Action<A350DrawCapture, DateTime, bool>? CaptainEfisDrawn { get; set; }`, raised once per tick with the tick's good captain EFIS draw, its time and whether the call-outs are settling after a load, inside the monitor's `Read("ND", ...)` guard, after the FMA read.
- `IniBuildsA350Definition.Hooks.cs`:
  ```csharp
  public partial class IniBuildsA350Definition
  {
      /// The captain's EFIS as the display monitor drew it this tick (a pool thread: never SimConnect, SIM-5, SIM-20).
      internal void ObserveCaptainEfis(A350DrawCapture capture, DateTime at, bool settling, Func<string, bool> muted,
          Action<string> speak) => ObserveNd(capture, at, settling, muted, speak);

      /// Plan 08's (IniBuildsA350Definition.Nd.cs); nothing until it is written.
      partial void ObserveNd(A350DrawCapture capture, DateTime at, bool settling, Func<string, bool> muted, Action<string> speak);
  }
  ```
- `StartA350DisplayService` sets `a350PfdMonitor.CaptainEfisDrawn` to call `a350.ObserveCaptainEfis` with the keyed mute and the posting speak it already gives the monitors, only while `currentAircraft` is that definition.
- [ ] **Steps 1-5** (tests `Each_good_captain_efis_draw_is_handed_on_once`, `A_trapped_efis_draw_is_not_handed_on`, `A_hook_that_throws_costs_only_its_own_read`); commit `refactor(a350): each good captain EFIS draw is handed to the definition`.

### Task 6: Land

- [ ] **Step 1:** Full suite green after the rebase (the index's workflow 4-5); the Ctrl+M dialog's rows for a definition are the same before and after (Task 3's first test).
- [ ] **Step 2:** In `00-index.md` "Base commits", replace "(plan 01 fills this in)" with the commit that lands this plan; commit `docs(a350): plan 01's base commit` with the trailer, push, set **Status** to landed.

## Review findings

(Task 1 fills this in.)

## Handoff to plan 21

- **Pilot-facing:** none from the seams; each fixed finding names its own.
- **Rule proposals:** none expected; a finding that shows a guardrail worth keeping proposes its line here.

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
