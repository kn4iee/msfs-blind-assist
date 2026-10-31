# A350 MFD Window Implementation Plan (plan 09, early: fix)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** The accessible MFD window (Shift+M) works the way the FBW A380's MCDU window does: page buttons and a "Go to MFD page" list reach every page, each actionable line says what it is (edit, button, combo box), the F-PLN and the other wordy pages read in a short, fixed form a braille display shows whole, long lists scroll the way a list does, and every KCCU key, Alt+D, Alt+M and the ATC pages in the air work.

**Architecture:** The window (`A350MfdWindow`) keeps its side box, page list, scratchpad and every key it has (Alt+letter page keys, Control+arrows the KCCU arrows, PageUp and PageDown, Enter, Backspace, F5). It gains the A380 window's two navigation aids: a row of page buttons carrying the window's existing Alt letters, and a "Go to MFD page" list of every page the A350's menus reach, each entry with its path (a KCCU page key, else the menu clicks), debounced as the A380's is. `A350MfdPage` gives each line its kind, so the window adds the A380's role words; its F-PLN rows become the A380's 40-cell grid under one column-header line; the colors follow owner question 12; long lists follow question 14, measured first.

**Tech Stack:** C# 13 / .NET 10 Windows Forms, xUnit with live-captured MFD fixtures; `tools/A350Probe` (`mfd`, `mfdlines`, `hotkey`).

**Spec:** `docs/a350/spec.md` sections 2.7 (the test flights' MFD findings), 3 (handoff 4: Alt+M; open checks: X12 KCCU), 4, 5 (questions 12 to 14); the A380 window `Forms/FBWA380/FBWA380MCDUForm.cs` (its header comment, `AllPages`, `RefreshDisplay`, `RoleWord`) and its F-PLN grid in `Resources/coherent-a380-agent.js` (`buildFplnLines`); `docs/inibuilds-a350.md` "The MFD window".

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply.
- Owned files: `Aircraft/A350/Display/A350MfdPage.cs`, `A350MfdInput.cs`, `A350MfdWindowMessages.cs`, `A350FmsMessageAnnouncer.cs`, `Aircraft/A350/A350MfdPages.cs` (new: the page directory), `Forms/A350/A350MfdWindow.cs`, `A350WindowsForm.cs`; tests `A350Mfd*Tests`, `A350FmsMessageTests`, new `A350MfdPagesTests.cs`, `A350MfdRolesTests.cs`, `A350MfdFplnGridTests.cs`, `A350MfdListTests.cs`; fixtures `p09_*.json`; `docs/inibuilds-a350.md` "The MFD window"; `docs/a350/evidence/09-mfd-window.md` (new).
- **No key moves.** Every key the window has keeps its meaning (the established-hotkeys rule): the A380 window's Control+Up and Control+Down page keys are NOT taken over, because on the A350 they are the KCCU's UP and DOWN. A new key is the owner's question first.
- The page buttons carry the window's existing Alt letters as their mnemonics (Alt+I INIT, Alt+F F-PLN, Alt+E PERF, Alt+L FUEL, Alt+R RAD NAV, Alt+P SEC, Alt+D DIR, Alt+A ATC, Alt+V SURV, Alt+C CLR, Alt+M MAILBOX, Alt+K C/L MENU, Alt+G PROG, Alt+T DEST, Alt+O DATA), each pressing the same KCCU key as today; the window's other Alt letters (N NOTEPAD, U MENU, B AIRPORT, Y REF, X ESC, S OIS) stay keys.
- Role words exactly as the A380 window's `RoleWord`: "edit" (a field taking typed input), "button", "combo box" with "collapsed"/"expanded", "option", "tab", "radio button", ", dimmed" for an unavailable one; a static text line carries none.
- The A380 F-PLN grid: ident 8, airway 6, track 5, distance 4, speed 5, altitude 7, time 5 cells, literal screen values (a ditto `"`, a constraint's `*` as drawn), the column-header line above the first waypoint, the descent FPA as a suffix; owner question 13.
- Owner questions 12 to 14 decide the colors, the grid and the lists; build the recommendations unless the Answer says otherwise.
- A list read whole (question 14) is read from the memory copy only ([A350-1]: a scroll offset poked in the copy, never in the sim); the real list moves only by the aircraft's own keys.

## Review Focus

- The "Go to MFD page" list arrowed through quickly: one page change once the selection settles, never one per arrow (Task 1 test, the A380's debounce).
- A page the menus show greyed (not available in this phase): its entry says so and nothing is pressed (Task 1 test).
- A field that is required and empty: "required, edit" (both), never one of them (Task 2 test).
- A torn read of a grid line: "unreadable", never a misaligned grid (Task 3 test).
- A list shorter than one screen: no scroll keys sent at all (Task 4 test).

---

### Task 0 [controller]: Claim this plan

**Who:** whoever runs the plan, before any other task; with subagents, the controller itself, never an implementer. A claim counts only once it is on the remote branch: one that exists only on your machine claims nothing, and if someone else's claim reaches the branch first, the plan is theirs.

- [ ] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/feat/inibuilds-a350 -- docs/a350/plans/09-mfd-window.md`. Anything but "not started" and "none" means the plan is taken: stop.
- [ ] **Step 2: Branch and claim.** Branch as in the index's workflow step 1. In this file's "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the commit you branched from. Commit only that change: `docs(a350): claim plan 09`, with the `Plan: 09` trailer.
- [ ] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/feat/inibuilds-a350`, `git push <remote> HEAD:feat/inibuilds-a350`. If the rebase stops on these lines, someone claimed the plan first: `git rebase --abort` and stop. If the push is rejected, repeat this step. Task 1 starts only once the push has gone through.

### Task 1 [sim]: Page buttons and the "Go to MFD page" list

**Files:** create `Aircraft/A350/A350MfdPages.cs`; modify `Forms/A350/A350MfdWindow.cs`; tests `A350MfdPagesTests.cs`; create `docs/a350/evidence/09-mfd-window.md`.

**Interfaces:**
```csharp
/// One page the A350's MFD menus reach: its name as the menus say it, and how a pilot gets there.
internal sealed record A350MfdPageNav(string Label, string? KccuKey, IReadOnlyList<(int X, int Y)> MenuClicks);
internal static class A350MfdPages
{
    public static readonly IReadOnlyList<A350MfdPageNav> All;   // menu order, "ACTIVE: F-PLN", "ACTIVE: PERF", ...
}
```
- [ ] **Step 1 [sim]:** Walk every MFD menu on 1.2.8 (the manual's menu map, PDF page 57, rendered), and record each page with its menu path (the clicks, from `A350Probe mfdlines live 0`'s click points) and its KCCU key where one exists; a page greyed in a phase is recorded with the phase.
- [ ] **Steps 2-6**, the usual five: `A350MfdPages.All` from Step 1; the window's page buttons (two rows after the scratchpad, as the A380's, each its Alt letter's KCCU key) and the "Go to MFD page" combo (`AccessibleName` "Go to MFD page", first in the tab order after the side box), navigating once its selection has settled (the A380's `_pageNavTimer`), by the KCCU key or the menu clicks, a greyed page said "{page} not available"; tests `Every_page_has_a_path`, `The_buttons_press_the_same_keys_as_the_alt_letters`, `A_settled_selection_navigates_once`, `A_greyed_page_presses_nothing`; commit `feat(a350): the MFD window's page buttons and page list`.

### Task 2: Each actionable line says what it is

**Files:** `A350MfdPage.cs` (each line's kind), `A350MfdWindow.cs` (the role word); test `A350MfdRolesTests.cs` on the existing MFD fixtures.

- [ ] **Steps 1-5** (tests `A_field_reads_edit` on INIT's FROM ("FROM: VCBI, cyan, edit" or as question 12 rules the color), `A_required_field_reads_required_edit`, `A_key_reads_button`, `A_list_opener_reads_combo_box_collapsed_or_expanded`, `A_greyed_key_reads_dimmed`, `A_static_text_has_no_role_word`); commit `fix(a350): the MFD window says which lines take input`.

### Task 3: The F-PLN as a grid, and the wordy pages short

**Files:** `A350MfdPage.cs`; tests `A350MfdFplnGridTests.cs`, `A350MfdPageTests.cs` (its F-PLN expectations move to the grid).

- [ ] **Steps 1-5** for the F-PLN (owner question 13): each waypoint row the A380's 40-cell grid with its header line, read from `mfd_fpln_route.json` and `mfd_fpln_basu1d.json` (tests `The_flight_plan_reads_as_the_a380s_grid`, `The_header_line_stands_above_the_first_waypoint`, `A_constraint_star_and_a_ditto_read_as_drawn`, `A_discontinuity_reads_as_one_short_line`, `The_grid_reads_the_same_under_de_DE`); commit `fix(a350): the MFD's F-PLN reads as the A380's grid`.
- [ ] **Step 6 [owner]:** The owner names the other pages that read too long (PERF, FUEL & LOAD, the DEPARTURE and ARRIVAL lists are candidates); each shortened to the A380 window's "label: value" form with the role word, test-first on its fixture (Steps 1-5 per page), commit `fix(a350): the MFD's <page> reads short`.

### Task 4 [sim]: Long lists scroll as a list does

**Files:** `A350MfdPage.cs`, `A350MfdInput.cs`, `A350MfdWindow.cs`; test `A350MfdListTests.cs`; fixtures `p09_mfd_departure_sid_<n>.json`.

- [ ] **Step 1 [sim]:** On DEPARTURE's runway and SID lists and ARRIVAL's STAR and approach lists, measure what the KCCU's DOWN (Control+Down) and the window's PageDown (`SCROLL_DOWN`) do: how many rows the list shows, how far one press moves it, when the window's next read shows the move (the 2026-10-31 flights needed many Control+Down presses before new rows appeared), and whether the list's rows are all in the memory copy.
- [ ] **Steps 2-6**, owner question 14's answer, the usual five (tests `The_whole_list_reads_from_the_copy` or `PageDown_moves_the_list_a_page_and_reads_it`, `Picking_a_row_moves_the_real_list_to_it_by_the_aircrafts_own_keys`, `A_short_list_sends_no_scroll_key`); commit `fix(a350): the MFD window's long lists`.

### Task 5 [sim, in flight]: The KCCU keys, Alt+D, Alt+M and the ATC pages

**Files:** fixes in `A350MfdInput.cs`, `A350MfdPage.cs`, `A350MfdWindow.cs` as found; fixtures `p09_mfd_atc_<page>.json`.

- [ ] **Step 1 [sim]:** The KCCU's arrows, BACKSPACE, OIS, MINUS and DIR from the window (`A350Probe mfd "key:0:…"`), each read back on the page; Alt+D against `INI_MCDU1_DIR_TO` and the DIR TO page, with and without a flight plan (X12).
- [ ] **Step 2 [in flight]:** Alt+M opens the mailbox as the guide says; every ATC COM REQUEST page (VERTICAL, LATERAL, SPEED, ADD TEXT, CLEARANCE with OCEANIC, DEPARTURE, GENERIC) and REPORT & MODIFY page opens, reads and types in the air (they are disabled on the ground); GENERIC's and the EMER keys' state on 1.2.8 recorded.
- [ ] **Step 3:** Each miss fixed test-first on its capture, commit `fix(a350): <key or page>`.

### Task 6 [sim]: The MFD's own checklists and pages on 1.2.8

- [ ] **Step 1:** C/L MENU's checklists read and tick through the window, end to end (the First Officer's checklists are plan 05's).
- [ ] **Step 2:** Each page a test pins re-captured on 1.2.8 (`A350Probe mfdlines live 0` with `A350_SAVE`); a page that reads differently is fixed test-first; record the differences.

## Handoff to plan 21

- **Pilot-facing:** "A350 MFD window: page buttons and a 'Go to MFD page' list; lines say edit, button or combo box; the F-PLN reads as a short grid; long lists scroll." Guide: the A350's MFD window section and its key list; the hotkey guide; changelog `improvement`.
- **Rule proposal:** if Task 4 reads lists from the copy, a new A350 rule: a hidden list row is read by moving the list in the copy only, and the real list only by the aircraft's own keys.

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
