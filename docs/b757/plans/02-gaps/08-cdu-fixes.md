# BlueBird 757 CDU Window Fixes Implementation Plan (plan 08, early: fixes)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** The 757 CDU window (Shift+M) stops moving the pilot's cursor: it stays on the pilot's line when a row above it appears or goes, and when an entry turns ACT into MOD; a real page change (a new page, NEXT PAGE) still speaks the page and moves to line 1. Then faster typing (owner, 2026-10-26): an entry typed in the scratchpad box goes out at the fastest key rate a measurement on the 757 shows reliable, and a key the CDU missed is said ("Scratchpad shows KAT").

**Architecture:** A new pure row builder turns a screen into typed rows (title, label, value, scratchpad) with a stable identity per row, as the MD-11's `Md11McduRows` does ([MD11-17]), so the window restores the cursor by row instead of by index. Page names and identities come from plan 01's `Bbs757CduTitle`. The row texts stay as they are today: plan 09 changes them. Typing: today every key is held 250 ms and the next goes 400 ms after its release, 0.65 s a character ("KATL" 2.6 s), a safe value never measured as the minimum (`docs/b757/evidence/cdu.md`, "Open for the CDU plan"). The tablet client gains a per-key gap, and the window's typed entries use a measured typing timing; every other key (the window's single keys, plan 01's `Bbs757CduPages`) keeps the standard 250/400, which plan 01's frozen tests pin. After a typed entry has gone out, the window checks the scratchpad holds it.

**Tech Stack:** C# 13 / .NET 10 Windows Forms, xUnit.

**Spec:** `docs/b757/spec-gaps.md` sections 4 (R68); `docs/b757.md` "CDU and EFB"; `docs/b757/evidence/cdu.md`; the CDU research in plan 09's header (the conventions across the app's CDU windows).

## Global Constraints

- Owned files (then passed to plan 09): `Forms/BBS757/BBS757CduForm.cs`, `Forms/BBS757/BBS757CduForm.Designer.cs`, `SimConnect/BBS757/Bbs757CduFrame.cs`, `SimConnect/BBS757/Bbs757CduKeys.cs`, `SimConnect/BBS757/Bbs757CduRows.cs` (new), `SimConnect/BBS757/Bbs757CduTyping.cs` (new); tests `Bbs757CduFrameTests.cs`, `Bbs757CduKeysTests.cs`, `Bbs757CduRowsTests.cs` (new), `Bbs757CduTypingTests.cs` (new); fixtures `tests/MSFSBlindAssist.Tests/Fixtures/bbs757-cdu-*.json`. Also (from plan 01, then passed to plan 13): `SimConnect/BBS757/CoherentBbs757TabletClient.cs` and its tests `CoherentBbs757TabletClientTests.cs`. And `docs/b757/evidence/flight-08.md` (Task 2's measurement).
- `Bbs757CduScreen.Lines` and `Bbs757CduKeys.ForChar` stay exactly as they are (plans 01, 04, 06 use them).
- The tablet client's `KeyHoldMs`, `KeyGapMs`, `KeyCycleMs` and its two- and three-argument `PressKey` keep their meaning: plan 01's `Bbs757CduPages` and its frozen tests use them. The agent (`Resources/coherent-bbs757-tablet-agent.js`, plan 13's next) is not changed: it already takes each key's hold, and the gap is the app's. Holds stay clamped 100-1500 ms (client and agent); a gap is clamped 50-1000 ms.
- Speech per CORE-7: a page change speaks the page; a new title on the same page (ACT → MOD, 1/2 → 1/3) is spoken too, as on every other aircraft's CDU window, and the cursor stays (owner, 2026-10-27); a cursor restore speaks nothing; a typed entry that landed is read back once as the scratchpad shows it (as every CDU window does and the guide promises; owner, 2026-10-27), one the CDU missed a key of is said once as "Scratchpad shows …". Keys always go out at once, as on every other aircraft: a key pressed before a typed entry is read back ends its check, and the ordinary read-back speaks what the scratchpad settles to.

## Review Focus

- A label row going blank above the cursor (a constraint deleted) keeps the cursor on the pilot's line (Task 1 test).
- ACT RTE 1 LEGS → MOD RTE 1 LEGS after an entry keeps the cursor on the line just edited (Task 1 test).
- NEXT PAGE (1/7 → 2/7) speaks the page and selects line 1's data row (Task 1 test).
- Fast typing that loses a key: the window says what the scratchpad shows ("Scratchpad shows KAT"), never stays silent; a scratchpad message over the entry is said as the scratchpad shows it (Task 2 test).
- Only typed entries use the typing timing: a page key, a line select key, CLR or DEL from the window, and every `Bbs757CduPages` key keep 250/400 (Task 2 test).
- Repeated characters ("11", "00") land at the typing timing: each needs the key released long enough to count twice (Task 2 step 1).

---

### Task 0 [controller]: Claim this plan

**Who:** whoever runs the plan, before any other task; with subagents, the controller itself, never an implementer. A claim counts only once it is on the remote `B757`: one that exists only on your machine claims nothing, and if someone else's claim reaches `B757` first, the plan is theirs.

- [x] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/B757 -- docs/b757/plans/02-gaps/08-cdu-fixes.md`. Anything but "not started" and "none" means the plan is taken: stop.
- [x] **Step 2: Branch and claim.** Branch as in the index's workflow step 1. In this file's "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the commit you branched from. Commit only that change: `docs(b757): claim plan 08`, with the `Plan: 08` trailer.
- [x] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/B757`, `git push <remote> HEAD:B757`. If the rebase stops on these lines, someone claimed the plan first: `git rebase --abort` and stop. If the push is rejected, repeat this step. Task 1 starts only once the push has gone through.

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

- [x] **Step 1: Write the failing tests** with fixtures from session 8's LEGS and NAV RADIO screens (add `bbs757-cdu-navradio.json`): `Kinds_and_lines`; `Restore_follows_the_row_when_a_label_above_goes`; `Act_to_mod_is_not_a_page_change`; `Next_page_is_a_page_change`; `PageStart_is_line_one_value`.
- [x] **Step 2: Run** `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter Bbs757CduRowsTests`. Expected: FAIL.
- [x] **Steps 3-4:** implement the builder and the window's use of it; full suite PASS.
- [x] **Step 5: Commit** `fix(b757): the CDU window keeps the cursor on the pilot's line`.

### Task 2: Faster scratchpad typing

**Files:** Modify `SimConnect/BBS757/CoherentBbs757TabletClient.cs`, `Forms/BBS757/BBS757CduForm.cs`; create `SimConnect/BBS757/Bbs757CduTyping.cs`; tests `CoherentBbs757TabletClientTests.cs`, `Bbs757CduTypingTests.cs`; `docs/b757/evidence/flight-08.md`.

- [x] **Step 1 [owner present; Claude may run it with the owner's go-ahead]: measure the fastest reliable key timing.** Done 2026-10-26 (`docs/b757/evidence/flight-08.md`): every pair typed 5/5, down to 100/50; chosen 100 ms hold, 100 ms gap. Not done: the repeat with the sim visibly loaded (optional; if a loaded sim drops keys, use 100/200). Also found: one CLR held 1300 ms cleared the scratchpad in 45 of 60 runs and removed only one character in the other 15, where a second long CLR always cleared it (`Bbs757CduPages`'s wipe now holds CLR a second time: plan 01's record). The 757 on the ground, BBS Nest running, MSFSBA closed (one inspector socket per view, [DBG-9]). In the `BBSTablet` view (`tools/coherent-eval.ps1 -Title BBSTablet`, with the tablet agent installed), type `KATL1100.5` into the first officer's CDU scratchpad by holding each key's L:var (`L:B757_CDU_2_<KEY>`) at 1 for H ms and waiting G ms after its release (one in-page `setTimeout` chain per run, so the timing is the page's), read line 13 with the agent's `scrape(2, "")`, then clear it with one CLR held 1300 ms and check it is blank. H in 250, 150, 100 (100 is the client's floor); G in 400, 200, 100, 50; five runs per pair. Read the stock `FRAME RATE` during each set, and repeat the fastest good pair with the sim visibly loaded (an outside view, a busy airport). Record every pair's hits and the frame rates in `flight-08.md`. Choose the fastest pair with 5/5 at every frame rate seen, then one step slower in G as margin. These writes only type into the first officer's scratchpad and clear it. The measurement script is not committed as a file (`docs/b757/tools/**` is frozen after plan 01): quote it in `flight-08.md`.
- **Interfaces:**
  - Client: `public void PressKey(int cdu, string key, int holdMs, int gapMs)`; the queue item carries the gap; the pump waits the key's hold plus its gap after the press; `internal static int CycleMs(int holdMs, int gapMs)` (both clamped). The two- and three-argument overloads keep `KeyGapMs`.
  - ```csharp
    public static class Bbs757CduTyping
    {
        public const int HoldMs = 100, GapMs = 100;   // step 1, flight-08.md
        public static IReadOnlyList<string> Keys(string text);                // Bbs757CduKeys.ForChar of each character, as today
        public static string? Mismatch(string typed, Bbs757CduScreen screen); // null when the scratchpad (trimmed) holds the typed text (trimmed, upper case); else "Scratchpad shows KAT" / "Scratchpad is empty"
    }
    ```
  - The window's `SendText`: each key through `PressKey(cdu, key, Bbs757CduTyping.HoldMs, Bbs757CduTyping.GapMs)`. Once the queue has drained and that CDU's next screen arrives (at most 2 s later), `Mismatch` is spoken once (`AnnounceImmediate`; CORE-7: the screen reader cannot tell a key was missed); a match is read back as the scratchpad shows it (`Bbs757CduTyping.ReadBack`, owner 2026-10-27: plan 08 first made it silent, which lost the read-back the guide promises). `SendKey` (page keys, line select keys, CLR, DEL) keeps the standard timing.
- [x] **Steps 2-6:** tests `Typing_keys_are_the_cdu_keys`, `Mismatch_cases` (a match is silent; a missed key; an empty scratchpad; a message over the scratchpad is said as shown), `Cycle_is_hold_plus_gap_clamped`, `Standard_keys_keep_250_and_400` (the two- and three-argument overloads, a client never started); run, implement, full suite; commit `feat(b757): the CDU window types faster and says a missed key`.

## In-sim checks (owner)

1. Shift+M on the LEGS page: enter a speed constraint on a line (ACT becomes MOD): "MOD RTE 1 LEGS …" is said and the cursor stays on that line; insert a waypoint until the page total changes (1/2 → 1/3): the title is said, the cursor stays; NEXT PAGE: the page is spoken and the cursor is on line 1; delete a constraint above the cursor: the cursor stays on its line. On a VNAV page (CLB or DES), enter a speed: the new title is said and the cursor stays on that line.
2. Shift+M, the scratchpad box (Alt+S): type `KATL` and Enter: it is in the scratchpad in about 1.6 s (0.4 s a key, see "Changed from the plan"; 2.6 s before), and BA reads it back once: "KATL". On RTE, type `KATL`, Enter and at once Ctrl+1: ORIGIN takes it, and nothing like "Scratchpad is empty" is said. Type a long entry while the sim is busy: if a key is missed, "Scratchpad shows …" is said.

## Handoff to plan 16

- **Pilot-facing:** "757 CDU window: the cursor stays on your line when the page changes around it, including when an entry turns ACT into MOD; a new page still moves you to line 1." Changelog `fix`. "757 CDU window: what you type in the scratchpad box goes into the CDU faster (about 0.4 s a character instead of 0.65 s), and if the CDU missed a key the window says what the scratchpad shows." Changelog `improvement`.
- **docs/b757.md:** the CDU section (row identity, page change rules, the typing timing and its measurement).
- **For plan 09 (takes the window over):** the typed-entry check (`Bbs757TypedEntryCheck`, pure, tested) reads the scratchpad back once after every typed entry (as shown when it landed, "Scratchpad shows …" when it did not, `Bbs757CduTyping.ReadBack`); any other key ends it, keys are never held; plan 09's CDU message speech (R72) should not say the same message twice. Plan 09's Global Constraints line "Plan 08's typed-entry check (spec R76) speaks the scratchpad once … when a typed entry did not land" is out of date (plan 09's file, so not edited here): it now speaks after every typed entry. A changed title is spoken on the same page too (never make ACT → MOD silent again: the owner's ruling). The three VNAV pages share one identity in `Bbs757CduRows` (their titles change with the speed mode) and are told apart by page number.
- **Also seen on (its own issue, not this plan):** the HS787 FMC window, which the 757 window was cloned from, still restores the cursor by index and jumps to line 1 on any title change.
- **Rule candidates (plan 09 or 16):** "restore the CDU cursor by row identity, never by index" and "never shorten `Bbs757CduTyping.GapMs` on one quiet measurement" (the review, 2026-10-27).
- **For plan 13 (takes the client over):** `PressKey` now has a four-argument form with the gap (clamped 50-1000 ms, `CycleMs`); the key queue carries it. Every other caller keeps 250 / 400.
- **Spoken text for the guide (plan 16):** a typed entry the CDU took is read back as the scratchpad shows it (*"KATL"*, the guide's "you hear what the CDU took", unchanged); one it did not says *"Scratchpad shows KAT"* (what the scratchpad shows, a CDU message included) or *"Scratchpad is empty"* (`Bbs757CduTyping.Mismatch`). A page change speaks the title with its spaces collapsed (`Bbs757CduTitle.Spoken`, e.g. *"ACT RTE 1 LEGS 1/2"*) and puts the cursor on line 1's data row; a new title on the same page (ACT turning into MOD, *"MOD RTE 1 LEGS 1/2"*, or a new page total) is said and the cursor stays. A key pressed before a typed entry is read back goes out at once; the scratchpad is then read as it settles (nothing when a line select key took the entry).

## Execution record

Kept in this file only, so no other plan's push ever touches it. Task 0 claims the plan (**Status**, **Claimed by**,
**Started**); fill in the rest as you go and when the plan lands (index, "Records").

- **Status:** waiting on owner (in-sim checks 1 and 2 in the app's Shift+M window, by ear; the code has landed)
- **Claimed by:** Declan Webster, 2026-10-26; taken back by the owner (kn4iee) 2026-10-27 to finish it
- **Worked by:** Declan Webster, with Claude (no subagents); 2026-10-27 the owner (kn4iee), with Claude: the
  scratchpad read-back fix, a whole-plan review (one review subagent) and its fixes
- **Started:** 2026-10-27, branched from the base commit `cf1dfce94` and rebased onto `B757` 744b4cce
- **Landed:** (date; the last commit of this plan on `B757`)
- **Tests at landing:** full suite passed 2026-10-27 after the rebase onto `B757` 744b4cce: 17,969 passed, 0 failed
- **Tasks:** 0 done (claim pushed 2026-10-26). 1 done: `Bbs757CduRows` (kinds, lines, `Restore`, `PageStart`,
  `IsPageChange`), the window restores the cursor by row and speaks only a real page change;
  `Bbs757CduFrame.ToDisplayLines` now lists the rows' texts, unchanged. 2 done: the client's four-argument
  `PressKey` and `CycleMs`, `Bbs757CduTyping`, the window's typed entries at the typing timing with the scratchpad
  check; step 1's measurement repeated on a busier sim (flight-08.md).
- **Waiting on the owner:** what can only be heard, in the Shift+M window: a page change speaks its title once, an
  ACT/MOD change speaks the new title, a typed entry is read back once ("KATL"), a missed key says "Scratchpad
  shows …", a line select key right after Enter says no false "Scratchpad is empty"; check 1's deleted constraint
  and page total change on LEGS (no route with constraints was loaded; the rules are pinned by
  `Bbs757CduRowsTests`); a speed entered on a VNAV page: the new title said, the cursor kept.
- **In-sim results:** 2026-10-27, MSFS 2024, BlueBird 757-200 PW 0.1.0, on the ground: aircraft powered with the EFB's
  Before start preset (APU, external power); the first officer's CDU's NAV RADIO frame captured live as the fixture
  `bbs757-cdu-navradio.json` and passing the row tests; the typing timing re-measured through the tablet agent's own
  press (flight-08.md, "Repeat on a busier sim"): 100/100 6 of 10, 100/200 15 of 15, 100/300 10 of 10. Then the
  app's own window, this branch's Debug build driven by Claude with UI Automation (MSFSBA as the only inspector
  client), captain's CDU at EGLL:
  - Check 1, passed for what it covers: Shift+M opened on IDENT with the cursor on line 1's data row; RTE (Alt+R)
    put it on line 1 again; with the cursor on line 6, entering EGLL (line 1) and EGCC (line 1 right) left it on
    line 6; ACTIVATE then EXEC turned the title "RTE 1" into "ACT RTE 1" while line 4 and line 6 changed, and the
    cursor stayed on line 6; NEXT PAGE (1/2 -> 2/2) and PREV PAGE (with the cursor on row 8) each put it on line 1's
    data row. A deleted constraint above the cursor was not tried (see "Waiting on the owner").
  - Check 2, passed for timing and delivery: `KATL1100.5` typed in the scratchpad box landed in full 4 times of 4,
    4.5-5.0 s from Enter to the window showing it (about 0.47 s a key with the window's round trip; 0.65 s before);
    EGLL and EGCC 1.7 and 1.9 s. Nothing was missed, so the missed-key line could not be heard; speech is the
    owner's to hear.
  The test left an EGLL-EGCC route active in the captain's CDU.
  2026-10-27 evening, the owner's flight in cruise (FL337-FL380, DELTA 773), MSFSBA closed except where said:
  - The read-back fix, this branch's Debug build, first officer's CDU on NAV RADIO: `5` then `7` typed in the
    scratchpad box were read back "5" and "57" about 1 s after Enter (debug.log: `CDU 2 typed entry '5': 5`,
    `'7': 57`). Claude drove that window with synthetic keys; the owner stopped it (they were using the PC), so the
    rest was not tried in the app. The "5I" it left (a key of the owner's reached the box) was cleared with a held
    CLR through the SimConnect MCP and the scratchpad read blank through the debugger.
  - The VNAV titles, read with page keys only (VNAV, PREV, NEXT, NAV RAD as L:var presses through the MCP, the
    screen through the debugger; the MCP cannot read CDU text): `               CLB  1/3 `, `     ACT ECON CRZ
    2/3`, `              DES   3/3 ` (DES showing SEL SPD 300). The first officer's CDU was put back on NAV RADIO.
- **Changed from the plan:** the typing gap is 300 ms, not 100 (flight-08.md): on a busier sim 100 / 100 lost a
  repeated digit in 4 of 10 entries; 100 / 200 never missed (15 of 15), and the margin step was taken half-way to
  the grid's 400 (100 / 300, 10 of 10), since 400 would leave typing nearly as slow as before. So the pilot-facing
  speed is 0.4 s a character, not 0.2 (in-sim check 2 and the handoff updated). `Bbs757CduTyping.Mismatch` takes an
  optional third argument, the scratchpad before the typing, because the CDU adds to an entry already there
  ("KA" + "TL" is a match, not "Scratchpad shows KATL"); `Expected` is new, the text the keys put there. A typed
  entry is checked INSTEAD of being read back by the window's `CduScratchpadAnnouncer` (which would otherwise say
  the entry the pilot just typed); the read-back is re-seeded silently after the check. (Reverted by the owner the
  same day: the check now reads a landed entry back itself; see "Rulings".) A second entry sent before
  the first was checked is checked with it, as one. `Bbs757CduRowKind.Blank` is declared as the plan's interface
  says but not produced (plan 09's). The tests were written with the code, not run red first.
- **Rulings made while working:** "Use the EFB's cockpit presets to power the aircraft for the checks?" Yes: Declan
  Webster, 2026-10-27. "Should a typed entry that landed stay silent?" No, it is read back: the owner (kn4iee),
  2026-10-27, after noticing "the scratchpad no longer reads when something is typed into it": "I'm reverting that
  decision because we really need to hear what's going in scratchpad". Task 2 had made a landed entry silent (the
  check replaced the read-back), against the guide's "you hear what the CDU took" and every other CDU window (HS787,
  PMDG 777/737, iFly, MD-11 read the scratchpad back after typing). `Bbs757CduTyping.ReadBack`: the scratchpad as shown
  when the entry landed, `Mismatch`'s "Scratchpad shows …" when it did not. Then two questions from the review,
  answered "However the other planes have it" (the owner, 2026-10-27): (1) a key pressed after Enter, before the
  read-back: the other CDU windows (PMDG 777/737, HS787, iFly, MD-11) send every key at once, never hold one, and
  have no missed-key check, so the 757 sends it at once and ends the check; the ordinary read-back speaks what the
  scratchpad settles to (not holding the line key until the entry is confirmed, which plan 01's Ctrl+N fill does).
  (2) A new page total (1/2 → 1/3): every other window speaks a changed title, ACT → MOD included, so the 757 now
  does too; on the cursor they split (PMDG, HS787, iFly jump to line 1 on any title change, the jump this plan
  fixes; the MD-11 moves it only for another page), so the 757 follows the MD-11: only another page or page number
  moves it.
- **Review (2026-10-27, one reviewer subagent over 744b4ccec..a6026ec9a; verdict "with fixes"):** strengths: the
  row restore follows [MD11-17], the client's per-key gap keeps `PendingKeyCount` above 0 through hold and gap, the
  standard 250/400 stays for every other caller, the 300 ms gap is justified by the busier-sim repeat. Fixed:
  (critical) typing then a line select key at once said "Scratchpad is empty" for an entry that landed, and a real
  miss followed by a line select read as success: any other key now ends the check (ruling 1 above); an entry typed
  behind other queued keys is not checked (what the scratchpad held is unknown), the ordinary read-back speaks it;
  (important) Ctrl+Tab during a check judged the other CDU's frozen screen (the client scrapes only the shown CDU)
  and muted the shown CDU's read-back: the switch now ends the check; (important) a page total change counted as a
  page change: `IsPageChange` compares the page number only (ruling 2); the check's state moved into the pure
  `Bbs757TypedEntryCheck` with tests (the review's recommendation); the client's summary and `MinGapMs` comments
  (a 50 ms floor is not a safe gap); a de-DE/sv-SE read-back test. Left: the VNAV retitle question (see "Waiting
  on the owner"), the rule candidates and the HS787 note (handoff). The VNAV question was then answered: "The
  title does change on vnav such as climb or descent when entering a speed" (the owner, 2026-10-27), so the three
  VNAV pages are one page identity told apart by page number (`Bbs757CduRows`; `Bbs757CduTitle` is plan 01's and
  frozen).
