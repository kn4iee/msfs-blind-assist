# BlueBird 757 EFB: Typing into the Tablet's Fields (plan 22, added 2026-11-06)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched. Tasks marked [controller] or [owner] are never dispatched to an implementer.

**Goal:** Every tablet field the window reads (plan 21's text boxes, and the value boxes where they bring up BlueBird's keyboard) is an edit box: Enter types the pilot's text into the tablet on BlueBird's own keyboard, and the field's new text is read back and said once.

**Architecture:** A pure planner, `Bbs757EfbTyping`, turns text into taps on the keyboard plan 20 reads (`Bbs757EfbScreenModel.Keyboard`: every key's three layers and tap point), with the layer keys it needs and the backspaces that clear the old text; `Bbs757EfbPressRunner.TypeAsync` runs it on a fresh read (relocate, bring into view, tap the field, confirm the keyboard is up, type in runs of at most 40 taps, Enter or the row's own save button, keyboard down, read back). The shared window gets a per-item opt-in: commit on Enter only.

**Tech Stack:** C# 13 / .NET 10, xUnit, the ES5 EFB agent (its `clickSeq` runs, unchanged), jsdom for the shell.

**Spec:** the owner's design of 2026-11-06 (`docs/superpowers/specs/2026-11-06-b757-efb-accessibility-design.md`, local), section 8; this plan is its phase 3. Plans 20 and 21 must have landed. Evidence: `docs/b757/evidence/efb-typing.md` (Task 1 writes it).

## Global Constraints

- Plan 20's Global Constraints apply, with the trailer `Plan: 22`.
- **D2:** a field shows what the tablet shows, the credentials included; the typed text and a field's value are never logged (a log line names the field only).
- **The text goes as typed:** no number parsing, no trimming, no case change; BlueBird validates it. A character no key carries stops the typing before any tap: "<field>: the tablet's keyboard has no <character>." (the app cannot type it: no refusal of a press, B757-19).
- **Leaving an edit box without Enter** changes nothing and says "<field> not changed" (the 757 EFB settings window's rule); Enter with the text unchanged types nothing and says nothing.
- **The agent:** at most 40 steps a run (`MAX_STEPS`), each delay at most 10,000 ms; one run at a time; a long text is several runs.
- **Taps that type** change the aircraft's state: every one in Tasks 1 and 7 waits for the owner's go-ahead, starts on a harmless field (PERF calc's Airport) and is put back after.
- **Owned files:** plan 20's and 21's, plus the new `SimConnect/BBS757/Bbs757EfbTyping.cs`.

## Review Focus

- **Text the field already holds part of** (KSEA typed over KSEA1): the field is cleared first, by Task 1's measured way, and reads exactly the typed text after (Task 3 test `Typing_replaces_the_whole_old_text`).
- **Upper and lower case and digits mixed** (a SayIntentions key "aB3x"): each character on its layer, the one-shot shift used once per capital, the numbers layer left before a letter (Task 2 test `Mixed_case_and_digits_take_the_right_layers`).
- **A text longer than one run** (a 32-character key): split into runs, no tap lost or doubled between them, the layer state carried across (Task 2 test `A_long_text_is_split_into_runs_without_losing_the_layer`).
- **The keyboard does not come up after the field's tap** (BlueBird took the tap elsewhere): nothing typed, "<field>: the tablet's keyboard did not come up." (Task 3 test `No_keyboard_types_nothing`).
- **The pilot leaves the edit box with changed text:** nothing typed, "<field> not changed", the box shows the tablet's text again (Task 4 jsdom test `Leaving_an_enter_only_box_restores_it`).

---

### Task 0 [controller]: Claim

- [ ] **Step 1:** Plans 20 and 21 read landed on `kn4iee/B757`. `git fetch kn4iee`; `git switch -c B757-p22-efb-typing kn4iee/B757` in this session's worktree; the Execution record: claimed, Claimed by, Started; commit only this file, `docs(b757): claim plan 22`; `git push kn4iee HEAD:B757`.

### Task 1 [controller, simulator up, MSFSBA closed, owner go-ahead]: Measure typing

**Files:** `docs/b757/evidence/efb-typing.md`

- [ ] **Step 0: Whether the keyboard is shown.** Plan 20 found `kb_grid`'s visible byte set on every page whether or not the keyboard shows, and no change in its app's bytes at a field tap or `btn_keyboard_down` (`efb-memory-pages.md`). Find the field that says it is shown (the efb object, the shell app's page, an animation offset, the top bar's "KEYBOARD LOCKED"), and how the numeric keypad that number fields open (the SimBrief ID, the TOD pause distance: `efb-and-manual.md`) is drawn; no key is tapped before this is known. The field goes into the layout file as plan 20 Task 8b did for the scroll offset.
- [ ] **Step 1:** PERF calc's Airport box: tap it; the keyboard's rectangle and the box's text in memory; whether the tap selects the text or puts the caret at its end (type one letter, read the text).
- [ ] **Step 2:** The layers: whether memory says which layer shows (diff the shell app before and after `btn_shift`, `btn_double_shift`, `btn_numeric_switch`); a capital after one shift (one-shot or held); a digit and a symbol on the third layer; what `btn_numeric_switch2` is; backspace on one character.
- [ ] **Step 3:** The fastest pace that types every character: runs of 10 letters at 300, 200 and 150 ms per tap, each read back.
- [ ] **Step 4:** Enter (`btn_enter`): whether it commits the box and closes the keyboard; whether the field's text changes after a commit (PERF calc may look the airport up). Then type the old text back.
- [ ] **Step 5:** The value boxes: tap `fuel_left_qty_box` and `wb_pax_business_weight_box`: BlueBird's keyboard, a keypad of its own, or nothing; leave without committing (keyboard down, or its cancel).
- [ ] **Step 6:** The SimBrief row, with the owner present: LOGOUT, the empty state's controls (its box and save button by name), the owner's ID typed back through them, read back as before. The Hoppie and SayIntentions boxes: their save behavior from their names and a read, nothing typed unless the owner asks.
- [ ] **Step 7:** Write the evidence (no credential value) and commit `docs(b757): how the tablet's keyboard types, measured`.

### Task 2: The typing planner

**Files:**
- Create: `MSFSBlindAssist/SimConnect/BBS757/Bbs757EfbTyping.cs`
- Test: `tests/MSFSBlindAssist.Tests/Bbs757EfbTypingTests.cs`

**Interfaces:**
- Consumes: `Bbs757EfbKeyboard`, `Bbs757EfbKey` (plan 20 Task 3).
- Produces: `public static class Bbs757EfbTyping` with `public const int MaxTapsPerRun = 40`; `public sealed record Bbs757EfbTypingPlan(IReadOnlyList<Point> Taps)`; `public static Bbs757EfbTypingPlan? Plan(Bbs757EfbKeyboard keyboard, string text, int clear, out char? missing)`: `clear` backspaces first (`btn_backspace`), then each character on its key: Lower as is, Upper after one `btn_shift` (the one-shot shift Task 1 measured; caps lock only if it measured faster), Third after `btn_numeric_switch` and back before the next letter, a space on `btn_space`; null with `missing` set when a character is on no key; `public static IReadOnlyList<IReadOnlyList<Point>> Runs(Bbs757EfbTypingPlan plan)` (consecutive slices of at most `MaxTapsPerRun`); `public const int TapGapMs` = Task 1's pace.

- [ ] **Step 1: Tests** (the keyboard from plan 20's `60-keyboard-up` tree): `Lower_case_letters_tap_their_keys`; `A_capital_takes_one_shift`; `Mixed_case_and_digits_take_the_right_layers`; `A_space_taps_the_space_bar`; `Clearing_taps_backspace_once_per_character`; `A_character_on_no_key_plans_nothing_and_names_it` ("é"); `A_long_text_is_split_into_runs_without_losing_the_layer`; `Nothing_to_type_and_nothing_to_clear_is_an_empty_plan`.
- [ ] **Step 2:** FAIL; implement; PASS. Commit `feat(b757): plan typing on the tablet's keyboard`.

### Task 3: TypeAsync

**Files:**
- Modify: `MSFSBlindAssist/SimConnect/BBS757/Bbs757EfbPressRunner.cs`, `Bbs757EfbPress.cs` (texts), `Bbs757EfbBridge.cs` (the run seam)
- Test: `tests/MSFSBlindAssist.Tests/Bbs757EfbPressTests.cs`

**Interfaces:**
- Consumes: `Bbs757EfbTyping` (Task 2); plan 20's `LocateAsync` (relocation and view).
- Produces: `Bbs757EfbPressIo` gains `required Func<IReadOnlyList<Point>, int, CancellationToken, Task<bool>> TapRun` (the points at one gap, one agent run; true when it went out); the bridge builds it over `CoherentBbs757EfbClient.RunAsync`. `public async Task TypeAsync(Bbs757EfbPressed pressed, string text, CancellationToken ct)`: text equal to the field's Value: nothing; locate and bring into view; tap the field; read until `Keyboard` is non-null (at most `ChangeMaxMs`), else `Bbs757EfbPress.NoKeyboardText(name)`; plan with `clear` = the Value's length (or 0 when Task 1 found the tap selects the text); `missing`: `Bbs757EfbPress.NoKeyText(name, ch)`, the keyboard closed, nothing typed; each run through `TapRun`; then `btn_enter`, or the row's own save button where Task 1 found one; `btn_keyboard_down` if still up; read until the field's Value changes (at most `ChangeMaxMs`): equal to the text: `Bbs757EfbPress.TypedText(name, value)` ("<name>: <value>") once; otherwise `Bbs757EfbPress.TypedOtherText(name, value)` ("<name> reads <value>.").

- [ ] **Step 1: Tests** (fakes): `Typing_replaces_the_whole_old_text`; `No_keyboard_types_nothing`; `A_missing_key_types_nothing_and_closes_the_keyboard`; `The_field_reading_the_text_is_said_once`; `The_field_reading_other_text_says_what_it_reads`; `Typing_the_text_already_there_taps_nothing`; `A_field_below_the_viewport_is_scrolled_to_first`; texts under sv-SE and de-DE.
- [ ] **Step 2:** FAIL; implement; PASS. Commit `feat(b757): type into a tablet field on its keyboard`.

### Task 4: The shell's Enter-only edit box

**Files:**
- Modify: `MSFSBlindAssist/Forms/FBWA380/FbwEfbForm.cs` (`PageHtml`'s `in` type, the item parse), `tools/flypad-shell-test/efb-shell.test.js`

**Interfaces:**
- Produces: an item key `commit` with the value `enter`: the shell's text box posts its `set` only on Enter; on blur with text other than the item's value it puts the item's value back and announces "<label> not changed"; with no `commit` key every EFB's box behaves as today. `FbwEfbForm` parses `items.N.commit` and passes it to the page.

- [ ] **Step 1: jsdom tests** (`npm test` in `tools/flypad-shell-test`): `An_enter_only_box_posts_on_enter_only`; `Leaving_an_enter_only_box_restores_it` (and announces "<label> not changed"); `A_box_without_commit_posts_on_change_as_before`.
- [ ] **Step 2:** FAIL; implement; PASS; the full app suite green. Commit `feat(efb): an opt-in Enter-only edit box in the shared EFB window`.

### Task 5: Fields become edit boxes

**Files:**
- Modify: `MSFSBlindAssist/SimConnect/BBS757/Bbs757EfbElements.cs`, `Bbs757EfbBridge.cs`
- Test: `tests/MSFSBlindAssist.Tests/Bbs757EfbElementsTests.cs`, `Bbs757EfbBridgeTests.cs`, the listings

**Interfaces:**
- Produces: a `Field` whose kind Task 1 showed brings up the keyboard (text boxes; the value boxes if it showed so) renders `controlType "text"`, `value` = its Value, `commit` = `enter`, named `Pretty(Text)`; other fields stay value lines. The bridge's `SetAsync` for `controlType "text"` calls `TypeAsync(pressed, value)`. The SimBrief row with an ID saved reads as the tablet shows it: a value line and the "SimBrief ID number: Logout" button (plan 20's R5); its empty box is an edit box.

- [ ] **Step 1: Tests:** `A_text_box_is_an_enter_only_edit_box`; `A_value_box_that_opens_no_keyboard_stays_a_line`; `A_set_on_an_edit_box_types_it`; `The_saved_simbrief_id_is_a_line_and_logout`; the listings updated (PERF calc, AIRAC data, Charts, Settings > Aircraft's TOD row, and Fuel and W&B if Task 1 showed so).
- [ ] **Step 2:** FAIL; implement; PASS; full suite green. Commit `feat(b757): the tablet's fields are edit boxes`.

### Task 6: Rules

**Files:** `.claude/rules/b757.md`, `docs/invariants/b757.md`

- [ ] **Step 1:** **B757-26:** "Type on the tablet only through `Bbs757EfbPressRunner.TypeAsync` on a fresh read: the field relocated and in view, BlueBird's keyboard seen up, the text on its keys (`Bbs757EfbTyping`), read back; never logged; a character on no key stops it before any tap." Budget and rules-hook tests green; commit `docs(b757): the typing rule`.

### Task 7 [controller, simulator up]: Live round trips, deploy, the owner's ears

- [ ] **Step 1 [owner go-ahead]:** PERF calc's Airport: KPDX typed, read back, KSEA typed back; a mixed-case text in a harmless box if one exists; the Hoppie or SayIntentions box only if the owner asks.
- [ ] **Step 2:** Deploy to `C:\MSFSBA-B757` as plan 20 Task 13 step 4.
- [ ] **Step 3 [owner]:** the in-sim checks below, one per message. Then push `HEAD:B757` (fetch, rebase, suite) and fill the Execution record.

## In-sim checks (owner, by ear)

1. PERF calc: Airport typed and its confirmation heard once; leaving a changed box without Enter says "Airport not changed".
2. AIRAC data: SimBrief ID logged out, typed in again, and heard back.

## Handoff to plan 16

- **Pilot-facing:** "The 757 tablet's fields are edit boxes: type, press Enter, and the app types it on the tablet's keyboard and reads it back; leaving a box without Enter changes nothing." Changelog `feature`; the guide's 757 EFB part (the SimBrief ID on the tablet as well as in the settings window).
- **docs/b757.md:** typing (`Bbs757EfbTyping`, `TypeAsync`, the Enter-only box).
- **CLAUDE.md, CORE-7's list of today's cases:** a typed field's read-back once.

## Execution record

- **Status:** claimed
- **Claimed by:** Robin Kipp (robin24), 2026-11-07
- **Worked by:** Robin Kipp with Claude (subagent-driven development)
- **Started:** 2026-11-07, from `B757` 8d8bacd18 (plans 20 and 21 landed)
- **Landed:**
- **Tests at landing:**
- **Tasks:**
- **Waiting on the owner:**
- **In-sim results:**
- **Changed from the plan:**
