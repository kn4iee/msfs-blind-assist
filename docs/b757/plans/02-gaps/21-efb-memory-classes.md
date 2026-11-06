# BlueBird 757 EFB: Text Boxes, Drop-downs, Lists, the Status Bar, Notifications and Questions from Memory (plan 21, added 2026-11-06)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched. Tasks marked [controller] or [owner] are never dispatched to an implementer.

**Goal:** The tablet window reads what plan 20's layout cannot: text boxes, the multi-line text box (the Operating FP), drop-downs and list boxes, which control is selected, where a slider's knob is, the top bar, BlueBird's notifications and its questions; drop-downs and list boxes can be chosen from.

**Architecture:** Measure first, read-only (Task 1). Every new offset goes into the layout file the generator writes ([WASM-2]): the app's `Bbs757EfbMemLayout` record gains the fields (Task 2), the private repo's `Bbs757EfbRecipe` derives the class vptrs by RTTI and carries the offsets as validated constants (Task 3), `Bbs757EfbMemory` reads them into its nodes (Task 4), the model reader turns them into plan 20's kinds plus `Choice` (Task 5), the model's `TopBar`, `Notices` and `Question` are filled (Task 6), and the press runner chooses from a drop-down by opening it (Task 7).

**Tech Stack:** C# 13 / .NET 10, xUnit, the private repo's toolkit (`MSFSBlindAssist.WasmToolkit`), the b757-memory probe scripts (`tools/*.py`, read-only).

**Spec:** the owner's design of 2026-11-06 (`docs/superpowers/specs/2026-11-06-b757-efb-accessibility-design.md`, local), section 7; this plan is its phase 2. Plan 20 must have landed (its fixtures, listings, kinds and rules are this plan's base). Evidence: `docs/b757/evidence/efb-memory-classes.md` (Task 1 writes it); `docs/b757/evidence/efb-memory-pages.md` (plan 20).

## Global Constraints

- Plan 20's Global Constraints apply (decisions D1-D7, the rulings, house rules, fixtures through `Bbs757EfbTreeScrub`, the simulator rule, the workflow), with the trailer `Plan: 21`.
- **Memory:** read only ([WASM-1]); no address, offset or table base in app code ([WASM-2]): every one in the layout file; a file without the new fields reads as missing ([WASM-26]) and the reader is unavailable until the regenerated file is there. The generator's output stays byte-identical for the same input ([WASM-21]).
- **Two repositories:** `MSFSBlindAssist.DisplayLayouts` is app-repo code: authored here (Task 2), then the app branch merged into the private worktree (`git fetch <app> +B757-p21-...` then `merge -X theirs`) so the recipe compiles (Task 3). The private repo's branch is `feature/b757-displays`; nothing there is pushed, and the layout is not published, without the owner's word.
- **Snapshots:** module memory snapshots can hold credentials (Navigraph, Hoppie, SayIntentions): kept only in the scratchpad, deleted after use, never committed in either repository.
- **Owned files:** plan 20's, plus `MSFSBlindAssist.DisplayLayouts/Bbs757/**`, `tests/MSFSBlindAssist.Tests/Bbs757LayoutFileTests.cs`, `Bbs757EfbMemoryTests.cs`, and in the private repo `toolkit/MSFSBlindAssist.WasmToolkit/Bbs757/Bbs757EfbRecipe.cs`, `toolkit/MSFSBlindAssist.WasmToolkit.Tests/Bbs757EfbRecipeTests.cs`, `Bbs757EfbFixture.cs`.
- **Taps that change the aircraft** (an OFP import for the defuel and units questions, a door or GPU tap for a notification, Automatic boarding off for START BOARDING, opening a drop-down and choosing) wait for the owner's go-ahead in chat; each is put back after it, and the settings files are compared with their copies from before (credentials by hash).

## Review Focus

- **A drop-down whose open list does not show the wanted entry** (it scrolls): the list is wheeled until the entry is in view, else nothing is tapped and "<name> could not be set to <word>." (Task 7 test `A_drop_down_entry_out_of_view_is_scrolled_to_or_not_chosen`).
- **A drop-down left open by the pilot's own mouse:** the next read shows its list as open; the choice taps the entry without opening it again (Task 7 test `An_open_drop_down_is_not_opened_again`).
- **A question that appears while a press runs:** the press's tap goes out only if its control is still on the page and not under the question (B757-18's relocation; Task 6 test `A_question_over_the_page_holds_back_a_page_press`).
- **The same notification words twice in a row** (a second CHOCKS ATTACHED.): spoken both times, each once (Task 6 test `A_repeated_notification_is_spoken_per_appearance`).
- **A layout file from plan 20's generator** (no new fields): reads as missing, the window shows why, nothing is guessed (Task 2 test `A_layout_without_the_phase_2_fields_reads_as_missing`).

---

### Task 0 [controller]: Claim

- [ ] **Step 1:** Plan 20's Status reads landed on `kn4iee/B757`. `git fetch kn4iee`; in this session's worktree at its head, `git switch -c B757-p21-efb-memory-classes kn4iee/B757`; this file's Execution record: claimed, Claimed by, Started; commit only this file, `docs(b757): claim plan 21`; `git push kn4iee HEAD:B757` (fetch and rebase on a rejection).

### Task 1 [controller, simulator up, MSFSBA closed]: Measure

**Files:** `docs/b757/evidence/efb-memory-classes.md`

- [ ] **Step 1: The classes.** With the b757-memory probe scripts (`C:\Claude\MSFSBA-wasm-displays\.claude\worktrees\b757-memory\tools`, read-only), for each of `bbos_control_textbox` (`hoppie_logon_code_textbox`, `planning_perf_calc_airport_input`, `planning_charts_request_input`), `bbos_control_multiline_textbox` (`planning_operating_fp_value`), `bbos_control_pulldownbox` (`planning_perf_calc_runway_dropdown`, `planning_charts_dropdown`), `bbos_control_listbox` (find one: the vptr in any front app's tree) and `bbos_control_image` (`chocks_status_image`): its runtime vptr, and the offset and shape of its text (a `std::string`, or a vector of them), a drop-down's current index and its entries, whether its list is open and where the open list's entries are drawn (their rectangles, or a first-row rectangle and a row height), a list box's entries and current one. Cross-check each against the tablet the owner or a capture shows.
- [ ] **Step 2: The selected state.** Diff a side-menu button's object bytes, current and not current, on three apps; and a tab's (Maintenance, PERF calc's TAKE OFF/LANDING). Record the field (offset, type, values), or that there is none.
- [ ] **Step 3: The slider knob.** Diff a settings slider's object at two choices and a cabin light slider at two places (the latter [owner go-ahead]); record the field (a float 0-1, a value, or the knob's x), or none.
- [ ] **Step 4: The top bar and notifications.** Find where the top bar's line lives (the probe of 2026-11-06 read "Fri, 6th Nov 2026 | 16:21L | 00:21Z | OAT 13 C"): the departure countdown, the boarding control, the notifications button; and the notification records (text, time, icon) and their container. [owner go-ahead] a door or GPU tap raises a notification; Automatic boarding off and an OFP fetch show START BOARDING and DEP.
- [ ] **Step 5 [owner go-ahead]: The questions.** IMPORT FROM SIMBRIEF with more fuel loaded than the plan (the defuel question), with the aircraft in metric units (the units question), and the refuel OK: where the box, its words and its answers live; answered as plan 18 answered them, and put back.
- [ ] **Step 6:** Write the evidence (no credential, no snapshot), and commit `docs(b757): the tablet's text boxes, drop-downs, lists, selection, status bar, notifications and questions in memory`.

### Task 2: The layout record and its file

**Files:**
- Modify: `MSFSBlindAssist.DisplayLayouts/Bbs757/Bbs757EfbMemLayout.cs`, `Bbs757LayoutDescriptions.cs`, `Bbs757Module.cs`
- Test: `tests/MSFSBlindAssist.Tests/Bbs757LayoutFileTests.cs`

**Interfaces:**
- Produces: `Bbs757EfbMemLayout` gains, after `CtrlButtonText`, one `int` per field Task 1 found, named by what it holds: `CtrlTextboxText`, `CtrlMultilineText`, `PulldownIndex`, `PulldownEntries`, `PulldownOpen`, `ListboxEntries`, `ListboxIndex`, `CtrlSelected`, `SliderKnob`, and the roots of the top bar, the notifications and the question box (names from the evidence); a field Task 1 found absent is not added. `ClassVptrs` gains the kinds `textbox`, `multiline`, `pulldown`, `listbox`, `image`. `Equals` and `GetHashCode` cover them.

- [ ] **Step 1: Tests:** `The_efb_layout_with_the_phase_2_fields_round_trips` (write, read, equal, and the text byte-identical on a second write); `A_layout_without_the_phase_2_fields_reads_as_missing`.
- [ ] **Step 2:** FAIL; implement (the file format through the existing `Bbs757Module.DescribeEfb` description); PASS; full suite green. Commit `feat(b757): the EFB layout carries the text box, drop-down, list, selection and status fields`.

### Task 3 [private repo]: The recipe derives them

**Files (private repo, worktree `C:\Claude\MSFSBA-wasm-displays\.claude\worktrees\b757-displays`):** `toolkit/MSFSBlindAssist.WasmToolkit/Bbs757/Bbs757EfbRecipe.cs`, `toolkit/MSFSBlindAssist.WasmToolkit.Tests/Bbs757EfbRecipeTests.cs`, `Bbs757EfbFixture.cs`

- [ ] **Step 1:** Merge the app branch in (Global Constraints). Tests: `The_recipe_finds_the_phase_2_class_vptrs` (`N3bbs20bbos_control_textboxE`, `N3bbs30bbos_control_multiline_textboxE`, `N3bbs24bbos_control_pulldownboxE`, `N3bbs20bbos_control_listboxE`, `N3bbs18bbos_control_imageE` → the vptrs Task 1 measured on BBS752.wasm SHA-256 357292c9…), `The_recipe_carries_the_phase_2_offsets` (each equal to Task 1's).
- [ ] **Step 2:** FAIL; implement (`TryGetClassVptr` as for the existing classes; the offsets as validated constants, as `CtrlLabelText` is); PASS; the toolkit suite green.
- [ ] **Step 3:** Regenerate the bbs757 layout from the installed BBS752.wasm; every field equals Task 1's; copy it to `%APPDATA%\MSFSBlindAssist\layouts\bbs757\` (the PowerShell tool: the Bash tool sees its own `%APPDATA%`). Commit on `feature/b757-displays`, local. Publishing (`toolkit/scripts/publish-layouts.ps1`) waits for the owner's word (Task 9).

### Task 4: The memory walk reads them

**Files:**
- Modify: `MSFSBlindAssist/SimConnect/BBS757/Bbs757EfbMemory.cs`, `tests/MSFSBlindAssist.Tests/Bbs757EfbTreeJson.cs`
- Test: `tests/MSFSBlindAssist.Tests/Bbs757EfbMemoryTests.cs`

**Interfaces:**
- Produces: `Bbs757EfbMemory.Node` gains `bool? Selected = null`, `string Value = ""` (a drop-down's or list box's current entry), `IReadOnlyList<string>? Entries = null` (its entries), `bool Open = false` (a drop-down's list shown), `float? Knob = null` (0-1); a text box's and multi-line text box's text goes in `Text`. `Bbs757EfbMemory.ReadTopBar() -> IReadOnlyList<Node>`, `ReadNotices() -> IReadOnlyList<(string Text, string? Time)>`, `ReadQuestion() -> (string Text, IReadOnlyList<Node> Answers)?` (shapes as Task 1 found them). The tree JSON writes and reads the new fields.

- [ ] **Step 1: Tests** with the fake `IEfbMemory`: `A_text_box_reads_its_text`, `A_multiline_box_reads_every_line`, `A_drop_down_reads_its_entries_and_current_one`, `A_list_box_reads_its_entries`, `A_selected_button_reads_selected`, `A_slider_reads_its_knob`, `The_notifications_read_text_and_time`, `A_torn_entries_vector_reads_none` (the vector bounds as `Vector` caps them).
- [ ] **Step 2:** FAIL; implement; PASS. Commit `feat(b757): the EFB memory walk reads text boxes, drop-downs, lists, selection, knobs and the status bar`.

### Task 5: The model from them; the name tables go where selection replaces them

**Files:**
- Modify: `MSFSBlindAssist/SimConnect/BBS757/Bbs757EfbModelReader.cs`, `Bbs757EfbPageRules.cs`, `Bbs757EfbScreen.cs` (the `Choice` kind), `Bbs757EfbElements.cs`
- Test: `tests/MSFSBlindAssist.Tests/Bbs757EfbModelReaderTests.cs`, `Bbs757EfbElementsTests.cs`, the listings (recaptured: Step 3)

**Interfaces:**
- Produces: `Bbs757EfbKind.Choice` (Value = the current entry, `Bbs757EfbItem` gains `IReadOnlyList<string>? Entries = null`). A text box is a `Field` (Text by R5, whose control words gain `input`, `textbox`, `dropdown`; Value = its text). A multi-line text box is `LongText`. A drop-down is a `Choice`. A list box's entries are `Button`s keyed `list:<name>|<entry>`, the current one `Selected`. When the layout has `CtrlSelected`: the section is the selected side-menu entry's word and a tab row's current tab is its selected tab; `Bbs757EfbPageRules.SectionFromTable` and the tab content match are deleted (the orders, field names and picture states stay). When it has `SliderKnob`: `Slider.KnobX = X + W × knob`. The window: a `Choice` is a `select` of its entries valued by its current one; a list box's entries are buttons, "(current)" after the current one.

- [ ] **Step 1: Tests:** `A_text_box_is_a_field_named_by_its_title` (`planning_perf_calc_airport_title` "Airport" + `planning_perf_calc_airport_input` "KSEA"); `The_operating_fp_is_long_text`; `A_drop_down_is_a_choice`; `A_list_box_is_its_entries`; `The_selected_menu_entry_is_the_section` (also Livery options in `aircraft_stackbox`); `The_selected_tab_is_current`; `A_knob_places_the_slider`.
- [ ] **Step 2:** FAIL; implement; PASS.
- [ ] **Step 3 [controller, simulator up]:** recapture every plan 20 page through the new layout (scrubbed: the scrub also makes up `planning_perf_calc_*` text boxes' and the OFP's text), update the listings, read the diff (PERF calc, Operating FP, Charts, AIRAC data's boxes, Settings > Aircraft's TOD row); full suite green. Commit `feat(b757): text boxes, the flight plan, drop-downs and lists on the tablet window`.

### Task 6: The status bar, notifications and questions

**Files:**
- Modify: `MSFSBlindAssist/SimConnect/BBS757/Bbs757EfbModelReader.cs`, `Bbs757EfbTopBar.cs`, `Bbs757EfbNotices.cs`
- Test: `tests/MSFSBlindAssist.Tests/Bbs757EfbModelReaderTests.cs`, `Bbs757EfbBridgeTests.cs`, `Bbs757EfbTopBarTests.cs`, `Bbs757EfbNoticesTests.cs`; fixtures `13c-aircraft-ground-services-notification`, `12b-aircraft-fuel-start-boarding`, `11b-aircraft-fuel-defuel-question`, `11c-aircraft-fuel-units-question` (Task 1's captures, scrubbed)

**Interfaces:**
- Produces: the model's `TopBar` keyed `top:dep`, `top:boarding`, `top:notifications` (`Bbs757EfbTopBar`'s keys, words through its `DepartureText`/`BoardingText`); `Notices` from `ReadNotices`; `Question` from `ReadQuestion` with its answers as Buttons (their rectangles for the taps). The bridge's `Bbs757EfbReadNews` and the press runner's question rules are unchanged: they get, from memory, what OCR gave them.

- [ ] **Step 1: Tests:** `The_top_bar_reads_departure_boarding_and_notifications`; `A_notification_reads_its_words_and_time`; `A_question_reads_its_words_and_answers`; `A_question_over_the_page_holds_back_a_page_press`; `A_repeated_notification_is_spoken_per_appearance`; the listings of the four fixtures (question first; "Status bar" and "Notifications" headings after the content).
- [ ] **Step 2:** FAIL; implement; PASS; full suite green. Commit `feat(b757): the tablet's status bar, notifications and questions from memory`.

### Task 7: Choosing from a drop-down and a list

**Files:**
- Modify: `MSFSBlindAssist/SimConnect/BBS757/Bbs757EfbPressRunner.cs`, `Bbs757EfbBridge.cs`, `Bbs757EfbPress.cs`
- Test: `tests/MSFSBlindAssist.Tests/Bbs757EfbPressTests.cs`, `Bbs757EfbBridgeTests.cs`

**Interfaces:**
- Produces: `Bbs757EfbPressRunner.ChooseAsync(Bbs757EfbPressed pressed, string entry, CancellationToken ct)`: relocate and bring into view (plan 20's `LocateAsync`); the read shows it with `entry` already: nothing; not open: tap it, read until `Open` (at most `ChangeMaxMs`); the entry's tap point from the open list (Task 1's geometry), wheeled into view within the list when needed (`MaxScrollSteps`); tap; read until it is closed and its Value is `entry`; then `Bbs757EfbPress.ChoiceText(name, entry)` once; otherwise `CouldNotSetText(name, entry)`. The bridge's `SetAsync` for a `Choice` with `controlType "select"` calls it. A list box entry is a `ClickAsync`.

- [ ] **Step 1: Tests:** `A_drop_down_opens_and_its_entry_is_tapped`; `An_open_drop_down_is_not_opened_again`; `A_drop_down_entry_out_of_view_is_scrolled_to_or_not_chosen`; `A_drop_down_showing_the_entry_taps_nothing`; `A_choice_that_did_not_take_says_so`; texts under sv-SE and de-DE.
- [ ] **Step 2:** FAIL; implement; PASS; full suite green. Commit `feat(b757): choose from the tablet's drop-downs and lists`.

### Task 8: Rules

**Files:** `.claude/rules/b757.md`, `docs/invariants/b757.md`

- [ ] **Step 1:** B757-22's text: a section and a current tab come from the selected state when the layout has it, else from `Bbs757EfbPageRules`. **B757-25:** "Choose from a tablet drop-down only through `Bbs757EfbPressRunner.ChooseAsync`: opened by its tap, the entry tapped where the open list shows it, confirmed from a fresh read; never by writing memory (WASM-1)." Budget and rules-hook tests green; commit `docs(b757): the drop-down and selected-state rules`.

### Task 9 [controller, simulator up]: Live checks, publish, deploy, the owner's ears

- [ ] **Step 1:** Recapture and compare every listing; navigation and tabs (current from the selected state on every app).
- [ ] **Step 2 [owner go-ahead]:** PERF calc's Runway drop-down to another runway and back; a notification raised and heard in the window (a door, then closed again).
- [ ] **Step 3 [owner's word]:** publish the layout (`toolkit/scripts/publish-layouts.ps1`) and push `feature/b757-displays`.
- [ ] **Step 4:** Deploy to `C:\MSFSBA-B757` as plan 20 Task 13 step 4; the regenerated layout in `%APPDATA%\MSFSBlindAssist\layouts\bbs757\`.
- [ ] **Step 5 [owner]:** the in-sim checks below, one per message. Then push `HEAD:B757` (fetch, rebase, suite) and fill the Execution record.

## In-sim checks (owner, by ear)

1. Navigation, Operating FP: the flight plan read line by line with the arrow keys.
2. A question (the defuel one after IMPORT FROM SIMBRIEF): heard once, its answers first, the answer takes.
3. PERF calc: a drop-down's choice list and its choice heard once.

## Handoff to plan 16

- **Pilot-facing:** "The 757 tablet window now reads the Operating FP, PERF calc's boxes and drop-downs, the charts list, the status bar, BlueBird's notifications and its questions, and lets you choose from its drop-downs." Changelog `improvement`; the guide's 757 EFB part.
- **docs/b757.md:** the phase 2 fields, the selected state, choosing from a drop-down.
- **docs/wasm-memory.md:** the EFB reader's new fields in the layout file.

## Execution record

- **Status:** not claimed
- **Claimed by:**
- **Worked by:**
- **Started:**
- **Landed:**
- **Tests at landing:**
- **Tasks:**
- **Waiting on the owner:**
- **In-sim results:**
- **Changed from the plan:**
