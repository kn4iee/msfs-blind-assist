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
- Plan 08's typed-entry check (spec R76) speaks the scratchpad once, while the window is open, when a typed entry did not land ("Scratchpad shows INVALID ENTRY"); R72's message speech is for the closed window. A message is never said twice.

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

- **Pilot-facing:** "757 CDU window: rows read 'Title:', the label rows, 'n:' lines and 'Scratchpad:' like the other Boeings, separator rows of dashes are gone, a blank screen says so, Alt+C clears the scratchpad, Ctrl+Shift+L and R pick the captain's or first officer's CDU, and the window's title names the side; CDU messages on the captain's CDU are spoken while the window is closed (a check box turns this off)." Until the `MainForm.BBS757.cs` request below is done, the last clause holds only once the window has been opened since the 757 loaded: say so in the guide, or do the request first. Guide chapter: `guide/boeing-aircraft-and-the-md-11.md` (the 757 CDU section, about line 456). Selections: BlueBird showed them as the CDU's own text (`<ACT>`) on every page read (flight-09.md), which the window already reads; an inverse (highlighted) option would read with "X ", but none was seen, so the guide should not promise X. Changelog `improvement` and `feature`.
- **Hotkey guide:** the CDU section ("CDU (Shift+M)"): Alt+C (two long CLRs, or DEL when the scratchpad is already empty), Ctrl+Shift+L / Ctrl+Shift+R. These two chords are also global hotkeys while output or input hotkey mode is on (surroundings, landing rate, the A380 RMP); with no mode on the CDU window gets them, as the MD-11 McDU window does with the same chord.
- **Ctrl+M:** the new row "CDU Messages While Closed" (the window's check box changes the same entry); no settings key needed.
- **Requests for files plan 09 does not own:**
  - `MainForm.BBS757.cs`: the CDU window is created on the pilot's first Shift+M, so CDU messages are spoken only once the window has been opened at least once since the 757 loaded. Create the window hidden (or move its message watch) when the tablet client starts at aircraft load, so a message is heard before the first Shift+M.
  - `SimConnect/BBS757/CoherentBbs757TabletClient.cs` (plan 13's): its class comment says only `Bbs757CduPages.WipeScratchpadAsync` holds CLR 1300 ms; Alt+C now does too. A pending-key count per CDU would let Alt+C press DEL on an empty scratchpad while the other CDU still has keys queued (today it then sends two long CLRs, which change nothing and say nothing).
  - `SimConnect/BBS757/Bbs757CduPages.cs` (plan 01, frozen): its private `WipeHoldMs` and `Bbs757CduKeys.ClearAllHoldMs` are the same measured 1300 ms; one could use the other.
- **Proposed rule lines** (`.claude/rules/b757.md`, full text in `docs/invariants/b757.md`):
  - "While hidden, the CDU window points the tablet client at the captain's CDU (`ActiveCdu = 1`) and judges each new captain scratchpad with `Bbs757CduMessageWatch`: never stop that hidden scrape, and never speak a change inside the watch's settle window after the window's own keys, or the pilot's typing is read out as a CDU message (R72)."
  - "A CDU row's selection marker (`Bbs757CduMarkers.Mark`) never moves a later column: it takes the blanks in front of the run or is paid back by the next gap, and a row stays within the 40-cell braille line."

## Execution record

Kept in this file only, so no other plan's push ever touches it. Task 0 claims the plan (**Status**, **Claimed by**,
**Started**); fill in the rest as you go and when the plan lands (index, "Records").

- **Status:** done on the branch `B757-p09-cdu-presentation` (2026-10-28), not pushed yet; then waiting on the owner:
  in-sim checks 1 and 2
- **Claimed by:** kn4iee (the owner), 2026-10-28
- **Worked by:** kn4iee (the owner) with Claude, with subagents (superpowers:subagent-driven-development: a fresh
  implementer and a reviewer per task, a whole-branch review at the end)
- **Started:** 2026-10-28; `B757` at 29091df7c
- **Landed:** (date; the last commit of this plan on `B757`)
- **Tests at landing:** full suite passed, 18,887 / 0 failed, after the rebase onto `B757` c108941d4 (the final fix
  wave's run)
- **Tasks:** 0-5 done. Task 4 step 1 (owner-in-sim) was measured live by Claude at the owner's request, in cruise, over
  the simconnect MCP and the Coherent debugger (`docs/b757/evidence/flight-09.md`).
- **Waiting on the owner:** in-sim checks 1 and 2, with these notes: open and close the window once before testing
  messages while closed (see the request for `MainForm.BBS757.cs`); "a selected option reads with X" cannot be seen,
  as BlueBird sent no inverse cell on any page read; add Alt+C pressed twice in a row (ends "Cleared", never
  "DELETE"), Alt+C with two FMC messages queued (does the second long CLR dismiss the second message unheard? CLR on
  an empty scratchpad was measured with a 0.3 s press only), Ctrl+Shift+L and R with no hotkey mode on (is the side
  said twice, once by the window and once by the screen reader's title change?), closing on the first officer's CDU
  and reopening, D or Shift+D while a message arrives on the captain's CDU (it is spoken), and closing the window
  right after a line select key whose entry fails (the reply is not spoken: it falls inside the 2-second settle
  window and the typed-entry check stops on close; wait for the read-back before closing).
- **In-sim results:** none run (Task 4 step 1's measurement, 2026-10-28, is in flight-09.md).
- **Changed from the plan:**
  - Task 1: `Bbs757CduRows.Build(screen, cdu)` as planned; `Bbs757CduFrame.ToDisplayLines(screen, cdu = 1)` passes it
    through for the tests, and the window passes its CDU. A blank screen is one row of kind `Blank`, line 0 (so after a blank frame the
    cursor starts on the title when the page comes back).
  - Task 2: the legs fixture's page counter "1/2" is small in all three cells; the large "/" is the one in the
    constraint `---/------`. The test asserts the fixture as captured.
  - Task 3: Alt+C sends two long CLRs (1300 ms each), not one: plan 08 measured one long CLR clearing the whole
    scratchpad only 45 times in 60, and a CLR on an empty scratchpad does nothing (flight-09.md). DEL only when the
    scratchpad is known empty and no keys are queued (a second Alt+C while the first's keys are out would else
    leave DELETE). The decision is `Bbs757CduKeys.ClearScratchpad`, the side keys `Bbs757CduKeys.SideFor`.
  - Task 4: inverse runs only; no colour or size marks (flight-09.md: none carried a selection). Markers are kept up
    to the 40-cell braille line (37 cells before the 3-cell row prefix).
  - Task 5: a pure `Bbs757CduMessageWatch` around `NewMessage` holds the last scratchpad, a 2-second settle window
    after the window's own keys (a hidden scrape comes once a second, so a last key can land between two scrapes) and
    the mute; while hidden, the window points the client at the captain's CDU and puts it back when shown; keys sent
    to the first officer's CDU are not counted; the check box also refreshes when the window is activated. After the
    whole-branch review, keys pending in the client hold a message back only inside the window's own captain-key
    settle window: the client has one queue for both CDUs, and the first officer's keys (D, Shift+D, Ctrl+N's ILS
    read) had made a captain message silent for good. Alt+C and the side keys are named on the CLR and Switch CDU
    buttons.
  - Task 4 step 1 grew into a tour of every page the FO CDU opens (about 55, at the owner's question): still no inverse
    cell and green only (flight-09.md).
  - Plan 09 adds no variables.
- **Rulings made while working** (Claude, as the controller, unless noted):
  - Alt+C: two long CLRs, DEL only when known empty and no keys queued (above).
  - The marker cap is the 40-cell line, not 26 (a reviewer's finding).
  - R72's speech lives in the window, the plan's file; speech before the first Shift+M is handed to plan 16.
  - `NewMessage`: silent for a first reading, a cleared scratchpad, typing that grows the text, CLR or DEL taking
    characters off; a sighted pilot's first key on the aircraft's own CDU from an empty scratchpad can still be
    spoken (accepted: the blind pilot types in the window).
  - Ctrl+Shift+L or R on the CDU already shown says its name again and changes nothing else.
  - Only the window's own captain keys hold a message back (the whole-branch review's finding). Left as is: the
    first push onto an empty captain scratchpad can still read as a message if two or more long CLRs the window did
    not count (the first officer's Alt+C, the page service's wipes) sit ahead of a short captain entry and the window
    closes within about 3.5 s: at worst one wrong "CDU: 2", never a lost message.
