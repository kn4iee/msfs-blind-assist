# BlueBird 757 CDU Window Fixes Implementation Plan (plan 08, early: fixes)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** The 757 CDU window (Shift+M) stops moving the pilot's cursor: it stays on the pilot's line when a row above it appears or goes, and when an entry turns ACT into MOD; a real page change (a new page, NEXT PAGE) still speaks the page and moves to line 1.

**Architecture:** A new pure row builder turns a screen into typed rows (title, label, value, scratchpad) with a stable identity per row, as the MD-11's `Md11McduRows` does ([MD11-17]), so the window restores the cursor by row instead of by index. Page names and identities come from plan 01's `Bbs757CduTitle`. The row texts stay as they are today: plan 09 changes them.

**Tech Stack:** C# 13 / .NET 10 Windows Forms, xUnit.

**Spec:** `docs/b757/spec-gaps.md` sections 4 (R68); `docs/b757.md` "CDU and EFB"; `docs/b757/evidence/cdu.md`; the CDU research in plan 09's header (the conventions across the app's CDU windows).

## Global Constraints

- Owned files (then passed to plan 09): `Forms/BBS757/BBS757CduForm.cs`, `Forms/BBS757/BBS757CduForm.Designer.cs`, `SimConnect/BBS757/Bbs757CduFrame.cs`, `SimConnect/BBS757/Bbs757CduKeys.cs`, `SimConnect/BBS757/Bbs757CduRows.cs` (new); tests `Bbs757CduFrameTests.cs`, `Bbs757CduKeysTests.cs`, `Bbs757CduRowsTests.cs` (new); fixtures `tests/MSFSBlindAssist.Tests/Fixtures/bbs757-cdu-*.json`.
- `Bbs757CduScreen.Lines` and `Bbs757CduKeys.ForChar` stay exactly as they are (plans 01, 04, 06 use them).
- Speech per CORE-7: a page change speaks the page; a cursor restore speaks nothing.

## Review Focus

- A label row going blank above the cursor (a constraint deleted) keeps the cursor on the pilot's line (Task 1 test).
- ACT RTE 1 LEGS → MOD RTE 1 LEGS after an entry keeps the cursor on the line just edited (Task 1 test).
- NEXT PAGE (1/7 → 2/7) speaks the page and selects line 1's data row (Task 1 test).

---

### Task 0 [controller]: Claim this plan

**Who:** whoever runs the plan, before any other task; with subagents, the controller itself, never an implementer. A claim counts only once it is on the remote `B757`: one that exists only on your machine claims nothing, and if someone else's claim reaches `B757` first, the plan is theirs.

- [ ] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/B757 -- docs/b757/plans/02-gaps/08-cdu-fixes.md`. Anything but "not started" and "none" means the plan is taken: stop.
- [ ] **Step 2: Branch and claim.** Branch as in the index's workflow step 1. In this file's "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the commit you branched from. Commit only that change: `docs(b757): claim plan 08`, with the `Plan: 08` trailer.
- [ ] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/B757`, `git push <remote> HEAD:B757`. If the rebase stops on these lines, someone claimed the plan first: `git rebase --abort` and stop. If the push is rejected, repeat this step. Task 1 starts only once the push has gone through.

### Task 1: Rows with identity, and the cursor that stays put

**Files:** Create `SimConnect/BBS757/Bbs757CduRows.cs`; modify `Forms/BBS757/BBS757CduForm.cs`; test `Bbs757CduRowsTests.cs`.

**Interfaces:**
  ```csharp
  public enum Bbs757CduRowKind { Title, Label, Value, Scratchpad, Blank }
  public sealed record Bbs757CduRow(Bbs757CduRowKind Kind, int Line, string Text);   // Line 1-6 for Label/Value, 0 otherwise
  public static class Bbs757CduRows
  {
      public static IReadOnlyList<Bbs757CduRow> Build(Bbs757CduScreen screen);   // today's texts, now with kinds
      public static int Restore(IReadOnlyList<Bbs757CduRow> before, int selected, IReadOnlyList<Bbs757CduRow> after);
      public static int PageStart(IReadOnlyList<Bbs757CduRow> rows);   // line 1's Value row, else 0
      public static bool IsPageChange(string oldTitleRow, string newTitleRow);   // Bbs757CduTitle.Identity or Counter differs
  }
  ```
  `Restore`: the row in `after` with the same kind and line as `before[selected]`; if gone, the nearest following row of the same line, else the same index clamped (the MD-11's `Md11McduRows` rule, [MD11-17]).
- The window: on a new screen, a page change (`IsPageChange`) speaks `Bbs757CduTitle.Spoken` and selects `PageStart`; a title change that is not a page change (ACT ↔ MOD) speaks nothing new and keeps the row by `Restore`; any other refresh keeps the row by `Restore`.
- Today's row texts stay (title raw, labels with three spaces, "n: data", scratchpad raw): plan 09 changes them.

- [ ] **Step 1: Write the failing tests** with fixtures from session 8's LEGS and NAV RADIO screens (add `bbs757-cdu-navradio.json`): `Kinds_and_lines`; `Restore_follows_the_row_when_a_label_above_goes`; `Act_to_mod_is_not_a_page_change`; `Next_page_is_a_page_change`; `PageStart_is_line_one_value`.
- [ ] **Step 2: Run** `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter Bbs757CduRowsTests`. Expected: FAIL.
- [ ] **Steps 3-4:** implement the builder and the window's use of it; full suite PASS.
- [ ] **Step 5: Commit** `fix(b757): the CDU window keeps the cursor on the pilot's line`.

## In-sim checks (owner)

1. Shift+M on the LEGS page: enter a speed constraint on a line (ACT becomes MOD): the cursor stays on that line; NEXT PAGE: the page is spoken and the cursor is on line 1; delete a constraint above the cursor: the cursor stays on its line.

## Handoff to plan 16

- **Pilot-facing:** "757 CDU window: the cursor stays on your line when the page changes around it, including when an entry turns ACT into MOD; a new page still moves you to line 1." Changelog `fix`.
- **docs/b757.md:** the CDU section (row identity, page change rules).

## Execution record

Kept in this file only, so no other plan's push ever touches it. Task 0 claims the plan (**Status**, **Claimed by**,
**Started**); fill in the rest as you go and when the plan lands (index, "Records").

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
