# BlueBird 757 Displays from Memory: the Upper EICAS (plan 24, added 2026-11-08)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched. Tasks marked [controller] or [owner] are never dispatched to an implementer.

**Goal:** Every warning, caution, advisory and communication message the upper EICAS shows is spoken as it appears and goes, and Alt+E opens an "Upper EICAS" window with the screen's engine indications, thrust reference, TAT and messages, read from `BBS752.wasm` memory; the AI read stays Alt+E's fallback.

**Architecture:** A reader behind plan 23's monitor (`IBbs757DisplayReader<Bbs757UpperEicasLayout, Bbs757UpperEicasReading>`), registered through the `RegisterUpperEicas` seam in a partial of its own. Task 1 traces each item the upper EICAS draws to its source (S10); the layout carries the message list's route and entry fields, the engine values' fields (or the simulator variables the draw takes), and the thrust reference's fields and words, all derived by a private recipe. The message walk is all-or-nothing.

**Tech Stack:** C# 13 / .NET 10, WinForms, xUnit, the private toolkit, the b757-memory probe scripts (read-only).

**Spec:** `docs/superpowers/specs/2026-11-08-b757-memory-display-readers-design.md` (local), §5 "Plan 24"; the decisions S1-S17 are in plan 23's "The series". Plan 23 must have landed. Evidence: `docs/b757/evidence/displays-upper-eicas.md` (Task 1 writes it); probe `findings.md` §3 "Upper EICAS messages" and "Thrust reference".

## Global Constraints

- Plan 23's Global Constraints apply (memory, two repositories, snapshots, the simulator, house rules, workflow), with the trailer `Plan: 24` and the artifacts path `<worktree>/bin/b757-p24`.
- **Wording (S8):** "EICAS warning: <text>", "EICAS caution: <text>", "EICAS: <text>" for an advisory or communication message, "EICAS gone: <text>"; one level's new messages in one settle window share a line, joined by ", " ("EICAS: L PACK OFF, L ENG BLEED OFF"); a warning line is spoken before a caution line before the rest. BlueBird's text exactly, its leading indent dropped for speech and kept nowhere else.
- **Cancel (S9):** a message the CANC key hides is never spoken as gone; the change of BlueBird's cancel state is read back once ("EICAS: 3 messages cancelled"; after RECALL, "EICAS recall: " and the messages that came back).
- **Read from the draw's source (S10):** an engine value is read from memory only where the draw reads memory; where it takes a simulator variable, the reader takes the same variable (registered hidden, at most 6 added; Task 6 counts them).
- **Shared lines (S16):** this plan adds `upperEicas` at the end of `Bbs757Module.Description`'s readers and its recipe at the end of `Bbs757Recipes.All`, and takes the next `GeneratorVersion.Current` at landing (`00-index.md`, "Display readers").
- **Owned files:** new `MSFSBlindAssist/Services/BBS757/Bbs757UpperEicas*.cs`, `MSFSBlindAssist.DisplayLayouts/Bbs757/Bbs757UpperEicasLayout.cs`, its dump in `Bbs757LayoutDescriptions.cs` (one method), the one reader line in `Bbs757Module.cs`, `MSFSBlindAssist/Aircraft/BBS757Definition.UpperEicas.cs`, `tests/MSFSBlindAssist.Tests/Bbs757UpperEicas*Tests.cs`, `docs/b757/evidence/displays-upper-eicas.md`, its rule lines; private `toolkit/MSFSBlindAssist.WasmToolkit/Bbs757/Bbs757UpperEicasRecipe.cs`, its fixture and tests, the one line in `Bbs757Recipes.cs`.

## Review Focus

- **More messages than the screen holds:** every active message is read and listed, in BlueBird's order, with "Page 1 of 2" as drawn; a message on page 2 appearing is spoken like any other (Task 5 test `A_message_beyond_the_first_page_is_listed_and_spoken`).
- **A message that goes and comes back inside one settle window:** nothing spoken (Task 5 test `A_message_that_flickers_inside_the_settle_window_is_not_spoken`).
- **The pilot's CANC:** the hidden messages are not "gone"; one line says how many were cancelled; RECALL brings them back with one line (Task 5 test `Cancel_hides_without_gone_and_says_the_count_once`).
- **A torn list** (the vector reallocated mid-read, a text pointer outside the module, a length over the cap): the whole reading is corrupt, nothing spoken from it (Task 4 test `A_torn_list_is_one_corrupt_reading`).
- **The EICAS unpowered at a cold-and-dark load, then the battery:** nothing compared or spoken until 10 s after the screen lights; then the messages present become the baseline, silently (Task 5 test `Powering_up_makes_the_first_messages_a_silent_baseline`).

---

### Task 0 [controller]: Claim

- [ ] **Step 1:** Plan 23's Status reads landed. `git fetch kn4iee`; `git switch -c B757-p24-upper-eicas kn4iee/B757`; this file's Execution record: claimed, Claimed by, Started; commit only this file, `docs(b757): claim plan 24`; `git push kn4iee HEAD:B757`. Private worktree: `git fetch origin`, branch `feature/b757-displays` at its head.

### Task 1 [controller, simulator up, MSFSBA closed]: Measure

**Files:** `docs/b757/evidence/displays-upper-eicas.md`

- [ ] **Step 1: The message list's route.** In the current build, the active list (`std::vector<entry*>` at static 0xf0a64 in the probed build, its getter function 36345 returning the address) and the definition table: how a recipe finds them without names (the getter returning a constant in the static range, called from the EICAS draw, function 22920 in the probed build; or an `eicas_computer` field), each entry's text pointer, length, id and level offsets, and the level values (warning, caution, advisory, communication, status) with a live example of each the cockpit can raise on the ground without harm (PARKING BRAKE, L PACK OFF; a caution such as L ENG SHUTDOWN at engine shutdown).
- [ ] **Step 2: The order and the pages.** Live: the list's order against the screen's (a capture of the EICAS view, `PrintWindow`), how many lines one page holds, where the draw decides "PAGE n" and its words.
- [ ] **Step 3: Cancel and recall** (owner's go-ahead: CANC and RECALL on the EICAS control panel): whether cancelled messages leave the vector or carry a flag, and the field that says the screen is in recall; the words drawn.
- [ ] **Step 4: The rest of the screen.** For EPR, N1, EGT (per engine), TAT, TAI, REV and the in-flight start envelope: each draw's source (memory field, or simulator variable), format, blanking, and where the draw picks a color (amber, red) from readable fields. The thrust reference (`tmc+452`, `tmc+456` in the probed build): every word, the derate's format.
- [ ] **Step 5: Power and the gate.** The upper EICAS's power state (a flag the draw tests, or a bus variable) cold and dark, on battery, on ground power. A value for the gate (S3) that changes in flight and that the simulator also reports: N1 or EGT in memory against `TURB ENG N1:1` or `GENERAL ENG EXHAUST GAS TEMPERATURE:1` at an engine start (owner's start, or a recorded one), its tolerance.
- [ ] **Step 6:** Write the evidence (no snapshot); commit `docs(b757): plan 24's upper EICAS measurements`.

### Task 2: The layout record

**Files:**
- Create: `MSFSBlindAssist.DisplayLayouts/Bbs757/Bbs757UpperEicasLayout.cs`
- Modify: `MSFSBlindAssist.DisplayLayouts/Bbs757/Bbs757Module.cs` (`public const string UpperEicas = "upperEicas";` and its `LayoutReader<Bbs757UpperEicasLayout>` last), `MSFSBlindAssist.DisplayLayouts/Bbs757/Bbs757LayoutDescriptions.cs` (`DescribeUpperEicas`)
- Test: `tests/MSFSBlindAssist.Tests/Bbs757UpperEicasLayoutTests.cs`

**Interfaces:**
- Produces: `public sealed record Bbs757UpperEicasLayout(string SourceFile, uint AnchorAddress, byte[] Anchor, uint ManagerBase, uint ManagerSpan, …)` with the fields Task 1 recorded, named for what they hold: the active list's route (`ActiveList`), `EntryText`, `EntryLength`, `EntryLevel`, `EntryId`, `Bbs757WordTable Levels` (value → "warning", "caution", "advisory", "communication", "status"), `MaxMessages` (the walk's cap), `LinesPerPage`, the cancel and recall fields, each engine value as a `Bbs757EicasSource` (a memory field with its type, or a simulator variable name), `ThrustReference`, `Derate`, `Bbs757WordTable ThrustWords`, the power test; content equality as `Bbs757FmaLayout`'s.

- [ ] **Step 1: Write the failing tests:** `The_upper_eicas_layout_round_trips_byte_identical` (also under de-DE and sv-SE), `A_file_without_the_upper_eicas_reader_reads_as_missing`, `Every_757_reader_is_in_the_module_in_order` (fma, efb, then upperEicas, then the others as they land).
- [ ] **Step 2:** FAIL. **Step 3:** Implement. **Step 4:** PASS and the full suite. **Step 5:** Commit `feat(b757): the upper EICAS layout record`.

### Task 3 [private repo]: The recipe

**Files:** `toolkit/MSFSBlindAssist.WasmToolkit/Bbs757/Bbs757UpperEicasRecipe.cs`, `toolkit/MSFSBlindAssist.WasmToolkit/Bbs757/Bbs757Recipes.cs` (one line), `toolkit/MSFSBlindAssist.WasmToolkit.Tests/Bbs757UpperEicasFixture.cs`, `Bbs757UpperEicasRecipeTests.cs`

- [ ] **Step 1:** Merge the app branch in (`git fetch <app worktree> +B757-p24-upper-eicas`, `git merge -X theirs FETCH_HEAD`).
- [ ] **Step 2: Write the failing tests** on a synthetic fixture shaped like Task 1's findings: `Derives_the_active_list_and_entry_fields`, `Derives_the_level_words_from_the_draw`, `Derives_the_thrust_reference_words_and_derate`, `Derives_each_engine_values_source`, `A_second_list_getter_fails_with_its_step`.
- [ ] **Step 3:** FAIL. **Step 4:** Implement: every number from the file (the anchor and manager as the FMA recipe's; the list getter by its returned constant and its caller; words from the draw's UTF-32 constants), each step exactly once or a failure naming it. **Step 5:** PASS and the toolkit suite. **Step 6:** Generate from the installed `BBS752.wasm`: every field equals Task 1's; `fma` and `efb` unchanged; copy to `%APPDATA%\MSFSBlindAssist\layouts\bbs757\`. Commit `feat(bbs757): the upper EICAS recipe` (private, not pushed).

### Task 4: The sampler

**Files:** `MSFSBlindAssist/Services/BBS757/Bbs757UpperEicasReader.cs`, `Bbs757UpperEicasReading.cs`; test `tests/MSFSBlindAssist.Tests/Bbs757UpperEicasReaderTests.cs`

**Interfaces:**
- Produces: `public sealed record Bbs757UpperEicasReading(IReadOnlyList<Bbs757EicasMessage> Messages, int Page, int Pages, bool Recall, int Cancelled, IReadOnlyList<Bbs757EicasValue> Values, string? ThrustReference, bool Powered, bool Corrupt) : IBbs757Reading`; `public readonly record struct Bbs757EicasMessage(int Id, string Text, string Level)`; `public readonly record struct Bbs757EicasValue(string Label, string? Left, string? Right, string? Color)` (one per engine item Task 1 recorded, in screen order: "EPR", "N1", "EGT", then TAT, TAI and REV with `Right` null where the item has one value; each value as drawn, null where BlueBird blanks it; `Color` "amber", "red" or null); `Bbs757UpperEicasReader : IBbs757DisplaySampler<Bbs757UpperEicasReading>` over `ModuleMemory`, a lease and the layout.

- [ ] **Step 1: Write the failing tests** on `FakeProcessMemory` holding Task 1's live list placed through the layout: `Reads_the_live_list_in_order`, `A_torn_list_is_one_corrupt_reading` (Review Focus), `A_list_over_the_cap_is_corrupt`, `Text_is_read_exactly_with_its_indent`, `Reads_the_thrust_reference_and_derate`.
- [ ] **Step 2:** FAIL. **Step 3:** Implement inside one `ModuleMemory.RememberRegions()` scope ([WASM-15]); every pointer checked inside the module. **Step 4:** PASS. **Step 5:** Commit `feat(b757): read the upper EICAS from memory`.

### Task 5: The gate, the words and the reader

**Files:** `MSFSBlindAssist/Services/BBS757/Bbs757UpperEicasGate.cs`, `Bbs757UpperEicasSpeech.cs`, `Bbs757UpperEicasDisplay.cs`; tests `Bbs757UpperEicasGateTests.cs`, `Bbs757UpperEicasSpeechTests.cs`, `Bbs757UpperEicasDisplayTests.cs`

**Interfaces:**
- Produces: `Bbs757UpperEicasDisplay : IBbs757DisplayReader<Bbs757UpperEicasLayout, Bbs757UpperEicasReading>` with `Name = "EICAS"`, `LogChannel = "bbs757-upper-eicas"`, `TickMs = 250`, mute keys `MSFSBA_BBS757_EICAS_MESSAGES` ("EICAS messages"), `MSFSBA_BBS757_THRUST_REFERENCE` ("Thrust reference"), and `MSFSBA_BBS757_ENGINE_COLORS` ("Engine gauge colors") only if Task 1 found the colors; each line `Changes` returns carries its row's key (`Bbs757SpokenLine`), and the reader's `MuteKey`, which also mutes its status lines, is the messages row. `Bbs757UpperEicasSpeech.Rows(reading)`: the engine rows ("EPR: 1.42, 1.42", "N1: 92.1, 92.0", "EGT: 610, 605"), "Thrust reference: CLB", "TAT: +12", one row per message ("Warning: …", "Caution: …", "Advisory: …", "Communication: …"), "Page 1 of 2" when Pages > 1, "Messages: none" when there are none.

- [ ] **Step 1: Write the failing tests:** gate `Passes_when_n1_matches_within_3_s`, `Unpowered_is_not_lit`; speech `New_messages_of_one_level_share_a_line`, `Warnings_are_said_before_cautions_before_the_rest`, `A_message_going_is_EICAS_gone`, `Cancel_hides_without_gone_and_says_the_count_once` (Review Focus), `Recall_says_what_came_back`, `A_message_beyond_the_first_page_is_listed_and_spoken` (Review Focus), `A_thrust_reference_change_is_said_once`, `Numbers_format_as_drawn_under_de_DE_and_sv_SE`; display `A_message_that_flickers_inside_the_settle_window_is_not_spoken` (Review Focus, through the monitor), `Powering_up_makes_the_first_messages_a_silent_baseline` (Review Focus), `A_muted_thrust_row_says_no_thrust_line`.
- [ ] **Step 2:** FAIL. **Step 3:** Implement. **Step 4:** PASS. **Step 5:** Commit `feat(b757): the upper EICAS's gate and words`.

### Task 6: The window, Alt+E and Ctrl+M

**Files:** `MSFSBlindAssist/Aircraft/BBS757Definition.UpperEicas.cs`; test `tests/MSFSBlindAssist.Tests/Bbs757UpperEicasWiringTests.cs`

**Interfaces:**
- Consumes: plan 23's `Bbs757DisplayRegistry`, `Bbs757KeyContext`, `BBS757DisplayForm`, `Bbs757DisplayMonitor<,>`.
- Produces: `partial void RegisterUpperEicas(Bbs757DisplayRegistry r)`: adds the monitor, the Ctrl+M rows, and `HotkeyAction.ReadDisplayUpperECAM`: `KeyFallsBackToAi()` → `context.AiRead()`, else `BBS757DisplayForm("Upper EICAS", "Upper EICAS", monitor.View)`, made once and shown again (exits output hotkey mode first).

- [ ] **Step 1:** Register the gate's and S10's simulator variables hidden and `Continuous` where not already (count them, at most 6; [VAR-7]).
- [ ] **Step 2: Write the failing tests:** `Alt_E_opens_the_upper_eicas_window_while_the_reader_runs`, `Alt_E_falls_back_to_the_ai_read_when_unavailable`, `The_ctrl_m_rows_are_listed`, `Switching_away_stops_the_upper_eicas_monitor`.
- [ ] **Step 3:** FAIL. **Step 4:** Implement. **Step 5:** PASS and the full suite. **Step 6:** Commit `feat(b757): the upper EICAS window on Alt+E`.

### Task 7: Rules

- [ ] **Step 1:** In `.claude/rules/b757.md` and `docs/invariants/b757.md`, the next free numbers: the cancel rule (S9: `Bbs757UpperEicasSpeech` never says "gone" for a cancelled message; the count once) and the all-or-nothing walk (`Bbs757UpperEicasReader`). `ClaudeContextBudgetTests` green. Commit `docs(b757): the upper EICAS rules`.

### Task 8 [controller, simulator up]: Live checks, deploy, publish

- [ ] **Step 1:** Rebase onto `B757` and the private branch onto its head; take the next `GeneratorVersion.Current` if another display plan landed meanwhile; regenerate the 757 layout with every reader on `B757`; layout tests and the full suite green.
- [ ] **Step 2:** Read-only on the ground: the window's messages against the probe's `eicastab.py` reading (PARKING BRAKE; L PACK OFF if the owner sets it); `bbs757-upper-eicas.log` reaches Running; the FMA and the tablet still read.
- [ ] **Step 3:** Deploy to `C:\MSFSBA-B757` with the regenerated layout; ask the owner to push and publish.

## In-sim checks (owner, by ear)

1. Cold and dark to engines running: battery on, then the first messages are silent; a pack off and on heard "EICAS: L PACK OFF" and "EICAS gone: L PACK OFF".
2. An engine start: the thrust reference and the engine rows in Alt+E; any start message heard once.
3. CANC and RECALL: the count once, the recall line once, no "gone".
4. A flight: "Thrust reference: CLB" after takeoff, "CRZ" at cruise; Ctrl+M "EICAS messages" off silences the messages only.

## Handoff to plan 16

- **Pilot-facing:** "The 757's EICAS messages are spoken as they appear and go, and Alt+E opens the upper EICAS: engine indications, thrust reference and every message." Changelog `feature`; the guide's 757 displays part, its Ctrl+M rows.
- **docs/b757.md:** the upper EICAS reader; the "AI display reads" section: Alt+E's fallback.
- **HotkeyGuides/BBS757_Hotkeys.txt:** Alt+E's line ("the upper EICAS window; AI read when it cannot be read").

## Execution record

- **Status:** claimed (2026-11-09)
- **Claimed by:** Robin Kipp (robin24), 2026-11-09
- **Worked by:** Robin Kipp with Claude (subagent-driven development)
- **Started:** 2026-11-09, from `B757` 9c1ab1c0a (plan 23 landed, every check passed)
- **Landed:**
- **Tests at landing:**
- **Tasks:**
- **Waiting on the owner:**
- **In-sim results:**
- **Changed from the plan:**
- **Open at landing:**
