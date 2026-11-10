# BlueBird 757 Displays from Memory: the Lower EICAS (plan 25, added 2026-11-08)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched. Tasks marked [controller] or [owner] are never dispatched to an implementer.

**Goal:** Alt+S opens a "Lower EICAS" window with the page the lower EICAS shows (the secondary engine page, STATUS, or whatever else BlueBird draws there) and that page's rows, read from `BBS752.wasm` memory; a page change is read back once; status messages are spoken as they appear and go while STATUS shows. The AI read stays Alt+S's fallback.

**Architecture:** A reader behind plan 23's monitor (`IBbs757DisplayReader<Bbs757LowerEicasLayout, Bbs757LowerEicasReading>`), registered through the `RegisterLowerEicas` seam. Nothing on this screen is decoded yet: Task 1 finds which page is drawn and traces each page's items to their sources (S10); the layout carries one part per page, so a page the recipe cannot derive costs only its rows ("<page>: not read").

**Tech Stack:** C# 13 / .NET 10, WinForms, xUnit, the private toolkit, the b757-memory probe scripts (read-only).

**Spec:** `docs/superpowers/specs/2026-11-08-b757-memory-display-readers-design.md` (local), §5 "Plan 25"; the decisions S1-S17 are in plan 23's "The series". Plan 23 must have landed; it runs beside plans 24 and 26. Evidence: `docs/b757/evidence/displays-lower-eicas.md` (Task 1 writes it); probe `findings.md` §4 (the lower EICAS's functions in the probed build: 22986 flight controls, 23003/23004 STATUS, 22954/22955 engine page, 23007 brake temperatures, 23008 crossbleed start).

## Global Constraints

- Plan 23's Global Constraints apply, with the trailer `Plan: 25` and the artifacts path `<worktree>/bin/b757-p25`.
- **Status messages (S11):** spoken only while the STATUS page shows: "Status: <text>", "Status gone: <text>"; the messages present when STATUS comes up are the page's content, not news, so they are not spoken (the window lists them). BlueBird's words exactly.
- **Page changes:** any change of the page shown is said once after the settle, "Lower EICAS: STATUS page", "Lower EICAS: engine page", "Lower EICAS: blank" (BlueBird's page names, the words Task 1 records); the first reading after a start or reload is silent (S4).
- **Optional parts:** each page is an optional part of the layout (the 737 ND's APP/VOR pattern): a page whose part is missing lists "<page>: not read: <reason>" and speaks nothing of its own; the page names and the gate are required.
- **Read from the draw's source (S10)**; simulator variables the draw takes are registered hidden (at most 8 added; Task 6 counts them).
- **Shared lines (S16):** `lowerEicas` at the end of the module's readers and its recipe at the end of `Bbs757Recipes.All`; the next `GeneratorVersion.Current` at landing.
- **Owned files:** new `MSFSBlindAssist/Services/BBS757/Bbs757LowerEicas*.cs`, `MSFSBlindAssist.DisplayLayouts/Bbs757/Bbs757LowerEicasLayout.cs`, its dump in `Bbs757LayoutDescriptions.cs` (one method), its reader line in `Bbs757Module.cs`, `MSFSBlindAssist/Aircraft/BBS757Definition.LowerEicas.cs`, `tests/MSFSBlindAssist.Tests/Bbs757LowerEicas*Tests.cs`, `docs/b757/evidence/displays-lower-eicas.md`, its rule lines; private `Bbs757LowerEicasRecipe.cs`, its fixture and tests, its line in `Bbs757Recipes.cs`.

## Review Focus

- **The pilot opens STATUS with messages already there:** nothing spoken but the page change; the window lists them (Task 5 test `Messages_present_when_status_opens_are_not_spoken`).
- **A status message appearing while another page shows:** not spoken; spoken neither when STATUS later opens (Task 5 test `A_status_message_off_the_status_page_is_not_spoken`).
- **A page the layout could not derive:** its rows say "not read" with the reason, the other pages and the page names still read (Task 5 test `A_page_without_its_part_lists_not_read`).
- **The pages cycled fast with the ENG and STATUS keys:** one line for the page it rests on, not one per press (Task 5 test `Fast_page_changes_say_only_the_last`).
- **Numbers BlueBird blanks** (an engine not running, oil quantity invalid): the row shows what the screen shows, blank or dashes, never a stale number (Task 4 test `A_blanked_value_reads_as_drawn`).

---

### Task 0 [controller]: Claim

- [ ] **Step 1:** Plan 23's Status reads landed. `git fetch kn4iee`; `git switch -c B757-p25-lower-eicas kn4iee/B757`; this file's Execution record: claimed, Claimed by, Started; commit only this file, `docs(b757): claim plan 25`; `git push kn4iee HEAD:B757`. Private worktree at `feature/b757-displays`'s head.

### Task 1 [controller, simulator up, MSFSBA closed]: Measure

**Files:** `docs/b757/evidence/displays-lower-eicas.md`

- [ ] **Step 1: The page.** Where the lower EICAS's draw decides which page it shows (the `eicas_lower` object, static slot 0xf139c in the probed build, or the `eicas_computer`), every value and its page, and how a recipe finds the field without names. Live: cold and dark, battery, ground power, ENG pressed, STATUS pressed (owner's go-ahead: the ENG and STATUS keys only).
- [ ] **Step 2: Each page's items.** For every page Step 1 found: each item the draw prints (its words, format, blanking) and its source (memory field or simulator variable), with a capture of the EICAS view (`PrintWindow`) to compare. STATUS: where its message list lives (the probe guessed "the computer's other vector") and its entry fields; the page's values.
- [ ] **Step 3: Power and the gate.** The lower EICAS's power state; a value that changes in flight and that the simulator also reports, kept whatever the page (N2 or oil quantity against `TURB ENG N2:1` or `ENG OIL QUANTITY:1`), its tolerance. If there is none, say so: the reader does not ship (S3) and the plan stops here with the owner's word.
- [ ] **Step 4:** Write the evidence (no snapshot); commit `docs(b757): plan 25's lower EICAS measurements`.

### Task 2: The layout record

**Files:** create `MSFSBlindAssist.DisplayLayouts/Bbs757/Bbs757LowerEicasLayout.cs`; modify `Bbs757Module.cs` (`public const string LowerEicas = "lowerEicas";`, its reader last), `Bbs757LayoutDescriptions.cs` (`DescribeLowerEicas`); test `tests/MSFSBlindAssist.Tests/Bbs757LowerEicasLayoutTests.cs`

**Interfaces:**
- Produces: `Bbs757LowerEicasLayout` with the anchor and manager, the page field and `Bbs757WordTable PageNames`, the gate's field, and one nullable part per page Task 1 found (`Bbs757LowerEicasEnginePage? Engine`, `Bbs757LowerEicasStatusPage? Status`, … each with its fields), each null part with its reason (`string? EngineMissing`, …), content equality.

- [ ] **Step 1: Write the failing tests:** `The_lower_eicas_layout_round_trips_byte_identical` (de-DE, sv-SE), `A_missing_page_part_keeps_its_reason`, `A_file_without_the_lower_eicas_reader_reads_as_missing`.
- [ ] **Step 2:** FAIL. **Step 3:** Implement. **Step 4:** PASS and the full suite. **Step 5:** Commit `feat(b757): the lower EICAS layout record`.

### Task 3 [private repo]: The recipe

**Files:** `Bbs757LowerEicasRecipe.cs`, one line in `Bbs757Recipes.cs`, `Bbs757LowerEicasFixture.cs`, `Bbs757LowerEicasRecipeTests.cs`

- [ ] **Step 1:** Merge the app branch in. **Step 2: Write the failing tests:** `Derives_the_page_field_and_names`, `Derives_each_page_part`, `A_page_it_cannot_derive_is_a_missing_part_not_a_failure`, `No_gate_field_fails_the_reader`. **Step 3:** FAIL. **Step 4:** Implement (each step exactly once or a failure naming it; a page's failure costs only its part). **Step 5:** PASS and the toolkit suite. **Step 6:** Generate from the installed file; every field equals Task 1's; the other readers unchanged; copy to `%APPDATA%`. Commit `feat(bbs757): the lower EICAS recipe` (private, not pushed).

### Task 4: The sampler

**Files:** `MSFSBlindAssist/Services/BBS757/Bbs757LowerEicasReader.cs`, `Bbs757LowerEicasReading.cs`; test `Bbs757LowerEicasReaderTests.cs`

**Interfaces:**
- Produces: `public sealed record Bbs757LowerEicasReading(string? Page, IReadOnlyList<string> PageRows, IReadOnlyList<string> StatusMessages, bool Powered, bool Corrupt) : IBbs757Reading` (the rows as drawn, built by `Bbs757LowerEicasText` from the page part's values); `Bbs757LowerEicasReader : IBbs757DisplaySampler<Bbs757LowerEicasReading>`.

- [ ] **Step 1: Write the failing tests** on `FakeProcessMemory` with Task 1's live readings placed through the layout: `Reads_the_page_shown`, `Reads_the_engine_page_rows_as_drawn`, `Reads_the_status_messages_in_order`, `A_blanked_value_reads_as_drawn` (Review Focus), `A_torn_status_list_is_corrupt`.
- [ ] **Step 2:** FAIL. **Step 3:** Implement (one `RememberRegions` scope; all-or-nothing list walk). **Step 4:** PASS. **Step 5:** Commit `feat(b757): read the lower EICAS from memory`.

### Task 5: The gate, the words and the reader

**Files:** `Bbs757LowerEicasGate.cs`, `Bbs757LowerEicasText.cs`, `Bbs757LowerEicasDisplay.cs`; tests `Bbs757LowerEicasGateTests.cs`, `Bbs757LowerEicasTextTests.cs`, `Bbs757LowerEicasDisplayTests.cs`

**Interfaces:**
- Produces: `Bbs757LowerEicasDisplay` with `Name = "Lower EICAS"`, `LogChannel = "bbs757-lower-eicas"`, `TickMs = 250`, `MuteKey = "MSFSBA_BBS757_STATUS_MESSAGES"` (row "EICAS status messages"; page changes are read back under it too); `Rows`: "Page: STATUS" (BlueBird's word), then the page's rows; "Lower EICAS: blank" when nothing is drawn.

- [ ] **Step 1: Write the failing tests:** `Passes_when_n2_matches_within_3_s`; `A_page_change_is_said_once_after_settling`, `Fast_page_changes_say_only_the_last` (Review Focus), `Messages_present_when_status_opens_are_not_spoken` (Review Focus), `A_new_status_message_on_the_status_page_is_spoken`, `A_status_message_off_the_status_page_is_not_spoken` (Review Focus), `A_page_without_its_part_lists_not_read` (Review Focus), `Numbers_format_as_drawn_under_de_DE_and_sv_SE`.
- [ ] **Step 2:** FAIL. **Step 3:** Implement. **Step 4:** PASS. **Step 5:** Commit `feat(b757): the lower EICAS's gate and words`.

### Task 6: The window, Alt+S and Ctrl+M

**Files:** `MSFSBlindAssist/Aircraft/BBS757Definition.LowerEicas.cs`; test `Bbs757LowerEicasWiringTests.cs`

**Interfaces:**
- Produces: `partial void RegisterLowerEicas(Bbs757DisplayRegistry r)`: the monitor, the Ctrl+M row, and `HotkeyAction.ReadDisplayLowerECAM` (fallback `context.AiRead()`, else `BBS757DisplayForm("Lower EICAS", "Lower EICAS", monitor.View)`).

- [ ] **Step 1:** Register the hidden simulator variables (count, at most 8; [VAR-7]). **Step 2: Write the failing tests:** `Alt_S_opens_the_lower_eicas_window_while_the_reader_runs`, `Alt_S_falls_back_to_the_ai_read_when_unavailable`, `The_ctrl_m_row_is_listed`. **Step 3:** FAIL. **Step 4:** Implement. **Step 5:** PASS and the full suite. **Step 6:** Commit `feat(b757): the lower EICAS window on Alt+S`.

### Task 7: Rules

- [ ] **Step 1:** The next free B757 numbers: the STATUS-page rule (S11: `Bbs757LowerEicasDisplay` speaks status messages only while STATUS shows, never those present when it opens). `ClaudeContextBudgetTests` green. Commit `docs(b757): the lower EICAS rules`.

### Task 8 [controller, simulator up]: Live checks, deploy, publish

- [ ] **Step 1:** Rebase (app and private); the next `GeneratorVersion.Current` if another display plan landed; regenerate with every reader on `B757`; layout tests and the full suite green.
- [ ] **Step 2:** Read-only on the ground: each page's rows against a capture of the EICAS view; `bbs757-lower-eicas.log` reaches Running; the other readers still read.
- [ ] **Step 3:** Deploy to `C:\MSFSBA-B757` with the regenerated layout; ask the owner to push and publish.

## In-sim checks (owner, by ear)

1. ENG and STATUS: each page change heard once; Alt+S lists the page and its rows.
2. On STATUS, a status message raised (one the cockpit can cause harmlessly, recorded in Task 1): heard once; "Status gone" when it clears.
3. Ctrl+M "EICAS status messages" off: no page or status line.

## Handoff to plan 16

- **Pilot-facing:** "Alt+S opens the 757's lower EICAS: the page it shows and its values; status messages are spoken while STATUS shows." Changelog `feature`; the guide's 757 displays part.
- **docs/b757.md:** the lower EICAS reader; Alt+S's fallback. **HotkeyGuides/BBS757_Hotkeys.txt:** Alt+S's line.
- **The guide:** the window's rows (engine page: N2, N3 on the RR, FF, OIL PRESS, OIL TEMP, OIL QTY, VIB; STATUS: HYD QTY, HYD PRESS, APU, OXY PRESS, the control pointers in words, FF, the messages, BRAKE TEMP by wheel 1-8); the page words; the Ctrl+M row "EICAS status messages" mutes page changes and status lines; the panel's "EICAS L Status" is the ENGINE key and "EICAS R Status" the STATUS key.
- **For plan 23's owner (Robin):** three edits to plan 23's frozen files, needed by any later reader (plan 24 will meet the first two): `AddDisplayVariables`' `nameOnBatch`, `Bbs757DisplayMonitor`'s "not powered" row while verifying, and `Bbs757DisplaysWiringTests`' monitor count and gate-streaming assertions. Also: the settle does not restart on later changes (its comment says "held for 300 ms").
- **Rule:** B757-33 (status messages only while STATUS shows on both readings).

## Execution record

- **Status:** done on the branch, waiting on the owner: the push of `B757` and the private branch, the generator-5 757 layout's publication, and the in-sim checks (below)
- **Claimed by:** Claude, for the owner (kn4iee), 2026-11-09
- **Worked by:** Claude for kn4iee (subagent-driven development: an implementer and a reviewer per task, an opus whole-branch review)
- **Started:** 2026-11-09, from `B757` 8b04ea227 (plan 23 landed; plan 24 claimed by Robin); claim 674206787
- **Landed:** not yet. App branch `B757-p25-lower-eicas` (this commit's parent 20fe14a6c), nothing new on `kn4iee/B757` since the claim, so the push is a fast-forward. Private branch `B757-p25-lower-eicas` (8c3f22ee7, on `feature/b757-displays` c5730f146 with the app merged in at ad4fdb4c3), not pushed; nothing new on `origin/feature/b757-displays` since. The generator-5 757 layout `bbs757/39462c4a…-1b75e5d6dd88c029.json` (SHA-256 9a4296a8…800f) is installed in `%APPDATA%`, not published
- **Tests at landing:** 21,917 passed, 0 failed (app, 20fe14a6c); 535 passed, 0 failed (toolkit, 8c3f22ee7); the solution builds with 0 errors
- **Tasks:**
  - 0 done (674206787, pushed). 1 done by a research agent for the controller (e54e4d6e7, corrected at 509fc8dec and 20fe14a6c): three presses of the lower EICAS's ENGINE and STATUS keys at the owner's go-ahead ("The sim is up so test as needed"), the page left as found; no cold and dark (the aircraft was in cruise).
  - 2 done (450e114df; its missing-part log ec9d8327d). 3 done in three reviewed parts (private: 3a core c5cfb7e7f, 3b engine page d8484c636, 3c STATUS page 8c3f22ee7): all 72 lower EICAS members derive from the installed file and equal the evidence; fma and efb identical to the published generator-4 file. 4 done (acfff9df9, fix round 8dd88fc3e: pointers as stored, N2/N3 truncated through f32). 5 done (1ae06f332). 6 done (1033ab117, fix round 30b4b708b). 7 done (560d3efe8: B757-33).
  - The whole-branch review (opus): no Critical; its fix wave 013dbff9c..7bb849f6c (a dark display reads "not powered" while its reader verifies; a corrupt reading's cause is logged; OIL QTY clamped to 21), re-reviewed: all addressed. 3c then found HYD QTY behind a vector's begin pointer: 20fe14a6c, re-reviewed.
  - Task 6 Step 1: **3 simulator variables added**, hidden copies of `TURB ENG N2:1`, `TURB ENG N2:2` (off the batch: the Engines panel streams those names) and `L:BBS_ECAM_Mode`.
  - 8: Step 1 done (no rebase needed; generator 5; layout and suites green). Step 2 done read-only without starting MSFSBA: a throwaway harness ran the app's own `LayoutStore`, `ModuleSession` and sampler on the running simulator in cruise for 92 s (367 readings, none lost or corrupt), the gate passed at 7 simulator samples (HP spool within 0.001 %) and failed on 3 made-up wrong ones; engine page, STATUS page and engine page off read plausibly (STATUS 19:02:07, ENGINE 19:02:32, :42, :52; left on the engine page as found). Step 3: the Release build deployed to `C:\MSFSBA-B757` (this machine had no such folder; the owner's usual copy, the main folder's Release build, is untouched).
- **Waiting on the owner:** push `B757` and the private branch, and publish the generator-5 757 layout (`toolkit/scripts/publish-layouts.ps1 -Source` that file only) together: [WASM-26] makes every 757 reader (the FMA and the tablet included) unavailable on a build of this branch until that file is installed. The in-sim checks below, ideally starting from cold and dark with the engine page showing, then an engine start: the power bytes, the gate during a spool-up and the start FF formula were never measured.
- **In-sim results:** none yet (the owner's).
- **Changed from the plan:**
  - **Page words (no BlueBird title exists):** 1 "engine page", 2 "STATUS page", 3 "engine page off" (FF stays drawn on 3), 0 or other "blank"; the layout carries the page values by kind, not a word table.
  - **The gate:** the HP spool (RR data+24, PW data+16) against `TURB ENG N2:1`/`:2` within 0.1 % in 3 s, and the page field against `L:BBS_ECAM_Mode`.
  - **Not read:** the X-BLD cue (a computed condition); the six `A:CIRCUIT ON` reads (pointers and brake digits read as with breakers in); the STATUS page number (the window lists every message, newest first).
  - **Several status messages in one change share one line** ("Status: A, B").
  - **Flight-control pointers** (nothing printed) read as "RUD: 25 % right", "ELEV: 10 % up" by the screen's direction.
  - **Engine FF while starting** follows BlueBird's formula without its fuel-control and fire-switch L:vars.
  - **Plan 23's frozen files:** `AddDisplayVariables` keeps a copy off the batch when any Continuous variable streams its name (VAR-7's own definition); `Bbs757DisplayMonitor` shows "display not powered" while verifying a dark candidate; two wiring tests count the registry's monitors and prove each gate copy streams.
- **Open at landing (later work, none blocking):** a panel ENGINE or STATUS press says "EICAS L/R Status pressed" and then the page change (kept: the only feedback when STATUS steps its own pages); the shared monitor's 300 ms settle starts at the first change, so presses under 300 ms apart can give two lines; PW not measured; deferred test and robustness minors in the SDD ledger.
- **Rulings made while working:** in the SDD ledger (git-ignored, `.superpowers/sdd/25-displays-lower-eicas/progress.md`) and the owner's report.
