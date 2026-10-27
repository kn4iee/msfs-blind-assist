# BlueBird 757 CDU Window Presentation Implementation Plan (plan 09, later: new features)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** The 757 CDU window reads as well by speech and on a braille display as the best of the other aircraft's CDU windows: rows in the shared shape ("Title:", label rows, "n:" data rows, "Scratchpad:"), no separator rows of dashes, a blank screen said, clean page names, Alt+C and side keys, selected options marked, and CDU messages spoken while the window is closed.

**Architecture:** Builds on plan 08's row builder (rows with identity). `Bbs757CduFrame` also keeps each cell's colour, size and inverse flag. Page names come from plan 01's `Bbs757CduTitle`. The window keeps the HS787 key layout and the shared `DisplayList` and `CduScratchpadAnnouncer`.

**Tech Stack:** C# 13 / .NET 10 Windows Forms, xUnit.

**Spec:** `docs/b757/spec-gaps.md` sections 4 (R70-R72), 8 (Q6); `docs/b757.md` "CDU and EFB"; `docs/b757/evidence/cdu.md`; the owner's note on CDU parity and braille.

**Conventions found across the app's CDU windows** (PMDG 737/777, iFly, HS787, FBW A32NX, Fenix, MD-11): one list item per screen row through `Forms/DisplayList.cs` ([A320-15]); the title first; each line select key's label row indented three spaces and left out when blank, then its data row "n: data" (one number for both sides: Ctrl+n left, Alt+n right); the scratchpad last; interior spaces kept so right-hand values stay in their columns (FBW keeps column 24 for braille); trailing spaces dropped; rows of 27 cells fit a 40-cell braille line; "<" and ">" kept; "°" kept (one cell in 8-dot computer braille); a selected option marked "X " on the Boeings; only titles, scratchpad changes and background events spoken ([CORE-7]); status text in a read-only `TextBox` ([VAT-13]); PageUp/PageDown page ([A320-18]); Ctrl+Enter or Alt+E EXEC; Alt+S scratchpad box, Alt+Home display. Where they differ, this plan follows FBW, Fenix and MD-11 for the "Title:" and "Scratchpad:" prefixes, the MD-11 for the blank-screen row and the page counter, and the iFly for clearing the scratchpad.

## Global Constraints

- Owned files (from plan 08): `Forms/BBS757/BBS757CduForm.cs`, `BBS757CduForm.Designer.cs`, `SimConnect/BBS757/Bbs757CduFrame.cs`, `Bbs757CduKeys.cs`, `Bbs757CduRows.cs`, `Bbs757CduTyping.cs` (plan 08's typing timing and the missed-key check, test `Bbs757CduTypingTests.cs`), `Bbs757CduMarkers.cs` (new), `Aircraft/BBS757Definition.CduRows.cs` (new, the Ctrl+M row hook); tests `Bbs757CduFrameTests.cs`, `Bbs757CduKeysTests.cs`, `Bbs757CduRowsTests.cs`, `Bbs757CduMarkersTests.cs` (new); fixtures `bbs757-cdu-*.json`; `docs/b757/evidence/flight-09.md`.
- `Bbs757CduScreen.Lines` and `Bbs757CduKeys.ForChar` keep their meaning; new data is added, never changed.
- Rows never exceed 40 cells and never end in spaces; interior spaces are kept.
- Rulings (spec R71, R72): a label row made only of 20 or more dashes is dropped; CDU scratchpad messages are spoken while the window is closed, once each, with a check box in the window to turn it off.
- Plan 08's typed-entry check (spec R76) speaks the scratchpad once after every typed entry, while the window is open: as it shows it when the entry landed ("KATL"), "Scratchpad shows INVALID ENTRY" when it did not; R72's message speech is for the closed window. A message is never said twice.

## Review Focus

- A frame with fewer cells, bad cells or no title still yields rows and never throws (Task 2 test).
- A row with a marker keeps its right-hand values in their columns (Task 4 test).
- Typing in the scratchpad is never mistaken for a CDU message while the window is closed (Task 5 test).

---

### Task 0 [controller]: Claim this plan

**Who:** whoever runs the plan, before any other task; with subagents, the controller itself, never an implementer. A claim counts only once it is on the remote `B757`: one that exists only on your machine claims nothing, and if someone else's claim reaches `B757` first, the plan is theirs.

- [ ] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/B757 -- docs/b757/plans/02-gaps/09-cdu-presentation.md`. Anything but "not started" and "none" means the plan is taken: stop.
- [ ] **Step 2: Branch and claim.** Branch as in the index's workflow step 1. In this file's "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the commit you branched from. Commit only that change: `docs(b757): claim plan 09`, with the `Plan: 09` trailer.
- [ ] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/B757`, `git push <remote> HEAD:B757`. If the rebase stops on these lines, someone claimed the plan first: `git rebase --abort` and stop. If the push is rejected, repeat this step. Task 1 starts only once the push has gone through.

### Task 1: The shared row shape

**Files:** `Bbs757CduRows.cs`; tests `Bbs757CduRowsTests.cs`, `Bbs757CduFrameTests.cs` (rewrite the two tests that pin today's texts, about lines 158-187, to the new shape).

**Interfaces** (examples are session 8's screens):
- Title: "Title: " + `PageName`, then three spaces and the counter when there is one: `  ACT RTE 1 LEGS     1/7` → `Title: ACT RTE 1 LEGS   1/7`; `        NAV RADIO       ` → `Title: NAV RADIO`.
- Label rows: three spaces + the row, trailing spaces trimmed, interior kept: ` 095°HDG` → `    095°HDG`; `VOR L              VOR R` → `   VOR L              VOR R`. A label row of 20 or more dashes and nothing else is dropped (R71); dash runs inside data (`---/------`) stay.
- Value rows: `1: (INTC)        250/ 3370`, `1: 116.90AGSU        ------`; "n:" when blank.
- Scratchpad: "Scratchpad: " + the trimmed pad ("Scratchpad:" when empty).
- A blank screen: one row, "Captain CDU screen blank" / "First officer CDU screen blank" (`Build(screen, cdu)` gains the CDU number).

- [ ] **Steps 1-5** (tests `Legs_rows_exact`, `Navradio_rows_exact`, `Ident_separator_dropped`, `Blank_screen_row`, `No_row_over_40_cells_or_trailing_space`); commit `feat(b757): CDU rows in the shared Boeing shape, for braille`.

### Task 2: Keep each cell's colour, size and inverse

**Files:** `SimConnect/BBS757/Bbs757CduFrame.cs`; test `Bbs757CduFrameTests.cs`.

**Interfaces:** `public readonly record struct Bbs757CduCell(char Ch, char Color, bool Small, bool Inverse);` and `Bbs757CduScreen` gains `IReadOnlyList<IReadOnlyList<Bbs757CduCell>> Cells` (14 × 24, defaults for missing or bad cells); `Lines` unchanged.
- [ ] **Steps 1-5** (tests `Cells_from_the_legs_fixture` (the page counter small, "/" large), `Bad_cells_never_throw`, `Lines_unchanged`); commit `feat(b757): CDU frames keep colour, size and inverse`.

### Task 3: Keys and the window title

**Files:** `Forms/BBS757/BBS757CduForm.cs`, `BBS757CduForm.Designer.cs`; test `Bbs757CduKeysTests.cs` (only if a key list changes).

- Alt+C: clears the whole scratchpad (`CduPages`-style long CLR: one `PressKey(cdu, "KEYCLR", 1300)` on the window's own client call), or presses DEL when the scratchpad is already empty (the iFly's behaviour). Ctrl+Shift+L and Ctrl+Shift+R select the captain's and first officer's CDU (Ctrl+Tab stays). The window title names the side ("BlueBird 757 CDU (Captain)").
- [ ] **Steps 1-5** (tests on a pure key-mapping helper if one is added); commit `feat(b757): CDU window gains Alt+C and side keys, and names its side`.

### Task 4: Selected-option markers

**Files:** Create `SimConnect/BBS757/Bbs757CduMarkers.cs`; modify `Bbs757CduRows.cs`; tests `Bbs757CduMarkersTests.cs`; new fixtures.

- [ ] **Step 1 (owner-in-sim):** capture pages with inverse or coloured selections (a modified LEGS page, a page with a selected option such as TAKEOFF REF or a toggle page) through the debugger (save the frame JSON as fixtures); record which attribute marks a selection.
- **Interfaces:** `public static string Mark(IReadOnlyList<Bbs757CduCell> row)`: an inverse run gets "X " in front without moving the row's later columns (take a blank from the gap before it, or insert and pay it back from the next gap, as `Services/FbwMcduFormat.cs` keeps column 24); a blank inverse run gets no marker. Colour or size marks only if step 1 shows they carry a selection.
- [ ] **Steps 2-6:** tests `Inverse_run_marked`, `Columns_kept`, `Blank_inverse_unmarked`; run, implement, full suite; commit `feat(b757): CDU marks the selected option`.

### Task 5: CDU messages while the window is closed

**Files:** `Forms/BBS757/BBS757CduForm.cs`, `Aircraft/BBS757Definition.CduRows.cs` (new: implements plan 01's `AddCduMonitorRows`); test on a pure helper in `Bbs757CduRows.cs`.

- R72: while the window is hidden, the tablet client keeps scraping once a second; a new message in the captain's scratchpad that is not typed text (a CDU message such as "TAKEOFF DATA UPLINK", "INVALID ENTRY") is spoken once ("CDU: TAKEOFF DATA UPLINK"). `public static string? NewMessage(string? oldPad, string newPad, bool keysPending)` decides (null while keys are queued or the text grew by typing). The off switch is one Ctrl+M row, so it sits with every other background announcement and needs no new setting: `public const string MessagesMonitorKey = "BBS757_CDU_MESSAGES"` in `Bbs757CduRows`; `AddCduMonitorRows` adds `new MonitorRow(MessagesMonitorKey, "CDU Messages While Closed")`; the message is spoken only when the key is not in `BBS757DisabledMonitorVariablesSet` (this speech comes from the client's poll, outside MainForm's wrap, so it checks itself, [VAR-8]). R72's check box in the window ("Speak CDU messages when closed") shows and changes that same entry: it adds or removes the key in `SettingsManager.Current.BBS757DisabledMonitorVariables` and calls `SettingsManager.Save()`, as a Ctrl+M tick does. Default: spoken.
- [ ] **Steps 1-5** (tests `Message_spoken_once`, `Typing_is_not_a_message`, `Cleared_is_silent`, `Muted_in_ctrl_m_is_silent`, `Cdu_monitor_row_added`); commit `feat(b757): CDU messages spoken while the window is closed`.

## In-sim checks (owner)

1. Shift+M on LEGS and NAV RADIO: rows read "Title: …", "n: …", "Scratchpad: …" on speech and braille; IDENT shows no row of dashes; Alt+C clears the scratchpad.
2. A selected option reads with "X "; with the window closed, an ACARS uplink's "… UPLINK" message is spoken once.

## Handoff to plan 16

- **Pilot-facing:** "757 CDU window: rows read 'Title:', the label rows, 'n:' lines and 'Scratchpad:' like the other Boeings, separator rows of dashes are gone, a blank screen says so, Alt+C clears the scratchpad, Ctrl+Shift+L and R pick the captain's or first officer's CDU, selected options read with X, and CDU messages are spoken while the window is closed (a check box turns this off)." Changelog `improvement` and `feature`.
- **Hotkey guide:** the CDU section ("CDU (Shift+M)").
- **Ctrl+M:** the new row "CDU Messages While Closed" (the window's check box changes the same entry); no settings key needed.

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
