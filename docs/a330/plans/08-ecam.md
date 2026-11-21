# A330 ECAM Displays Implementation Plan (plan 08, later)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** Output Alt+E opens a live E/WD window and Output Alt+S a live lower ECAM (system display) window, each listing what the display draws, top to bottom, with the ECAM control panel's keys under the list (spec D9). Warnings, cautions and memos are called out as they appear and clear, each kind on its own Ctrl+M row. When the reader cannot run, both keys fall back to the AI read without a word.

**Architecture:** Plan 02's reader gives each display's drawn texts with positions, fonts and colours. A pure page model groups them into the display's areas by measured position (the E/WD's engine block, fuel line, left and right message areas; each system page's lines), and a pure text step turns the model into the window's rows. Numbers the display draws without a label are named from a small per-page table, each entry proven by a captured fixture. An announcer compares the message areas between complete frames. The windows are plan 02's `A330DisplayWindow` with their key rows.

**Tech Stack:** C# 13 / .NET 10 Windows Forms, xUnit; plan 02's `IA330Displays`, `A330DisplayWindow` and replay source; the reader program's `capture` command for the sim tasks.

**Spec:** `docs/a330/spec.md` sections 2.1, 2.6, 3 (R15), 5 (D3, D8, D9); `docs/ini-a330.md` "ECAM pages"; the Fenix's rules for the same displays as the yardstick for wording (`.claude/rules/fenix-channel-displays.md`: colours as the display gives them, an alert's clear after a hold, never a name for something not measured).

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply.
- Owned files: `Aircraft/IniA330Definition.Ecam.cs`, `Aircraft/A330/Ecam/**` (new), `Aircraft/A330/A330CalloutRows.Ecam.cs`, `Forms/IniA330/A330EwdWindow.cs`, `A330SdWindow.cs` (new), tests `tests/MSFSBlindAssist.Tests/A330/A330Ewd*Tests.cs`, `A330Sd*Tests.cs`, `A330Ecam*Tests.cs`, fixtures `p08_*.json`; `docs/ini-a330.md` "ECAM pages"; `docs/a330/evidence/08-ecam.md` (new).
- A row carries the display's own text. A number is named only by a label the display draws for it or by the per-page table; a text whose meaning is not established is listed as drawn, with where it is ("Lower left: XX, amber"), never guessed and never dropped.
- Needles, arcs, flow lines and symbols are graphics the reader does not see. A value shown only as graphics is said from a variable if the aircraft has one, and otherwise the page's list says "not read: shown as a symbol" for it once.
- Rows and call-outs come only from complete frames; a call-out only when two complete frames in a row agree.
- Colours follow plan 02's rule (spec M14): build and test on the 2026-11-20 fixtures, which carry colours. At run time, a frame with no colour gets no colour word, and the E/WD's alert call-outs, which tell a warning, a caution and a memo apart by colour, stay silent; the windows list their rows and say "Colours not read yet". The colour-based call-outs ship once M14 is solved or the owner rules otherwise.
- The window's keys press the ECAM panel's controls through `PressFromWindow` (plan 01) by their keys in plan 04's map, and show each key's state from `DescribeControl`.
- Colour words follow `A330Colours.Name` (plan 02); which rows say their colour follows spec D8's table, which follows the Fenix's windows: on the E/WD every message line (warning, caution, memo, action line) says its colour after its words, and an engine block value only when it is not its normal one; on the lower ECAM a value says a colour only when the page draws it in other than its normal one, and titles, labels and units never. The normal colours are measured from the fixtures (green values, white labels, cyan units), never assumed.

## Changed on 2026-11-21

The reader's approach changed (spec D14): the E/WD and the lower ECAM read whole as before, but frames
carry no colour until spec M14 is solved. The Global Constraints say what that means; Task 2 adds the
tests `With_no_colour_no_colour_word_is_said` and `With_no_colour_no_alert_is_called_out`.

## Measured on 2026-11-20

**Task 1 and Task 5's captures exist** (with the private reference; the fixtures are taken from them).

- **E/WD on batteries only:** complete, 27 texts: the engine fields as amber XX, FOB, the memo PARK BRK,
  and four cautions, each a title, the fault on the same line, and action lines in cyan under it:
  `VENT` / `EXTRACT FAULT` / ` -EXTRACT..............OVRD`; `FUEL` / `FCMC 2 FAULT` /
  `-FCMC 2........RESET`; `DATALINK` / `ATSU FAULT` / `ATC COM VOICE ONLY`; `DOOR` /
  `POS DET 1 + 2 FAULT`. The title and its fault are two texts starting at the same x (the fault's text
  begins with spaces): the page model joins them.
- **E/WD on external power:** memos IR IN ALIGN 6 MN, PARK BRK, ELEC EXT PWR in green; the cautions gone.
- **Colours:** amber 1.0 0.55 0.0, green 0.38 0.80 0.39, cyan 0.31 0.82 1.0, white.
- **Self test:** each display draws SELF TEST IN PROGRESS and MAX 40 SECONDS as text for its first 40 s:
  the announcer stays silent through it (the first page after start is silent already).
- **Lower ECAM:** ENG, BLEED, PRESS, EL/AC, EL/DC, HYD, APU, COND, DOOR, WHEEL, F/CTL and FUEL each drew
  their page, complete in 1.5 to 6 s. C/B left the page as it was. STS with nothing to report left
  DOOR/OXY (the page variable read 11). HYD and COND draw NO title text: the page's name comes from
  `INI_ECAM_ACTIVE_PAGE`, never from the frame. Every page ends with the same line: TAT, the gross weight
  and its unit, SAT, the UTC hour and minute, the gross weight CG.
- Most values on a page are numbers with no label beside them (the F/CTL page: 47 texts, mostly single
  letters and digits): Task 5's labels by position are the whole of that work.

## Review Focus

- A warning that appears and clears within a second (a test, a transient) spoken as appeared and never as cleared, or the reverse: the announcer's hold and its tests (Task 4).
- The same alert spoken twice because its line moved up when the one above it cleared: alerts are compared by text and colour, not by line (Task 4 test).
- A system page read with the previous page's label table during a page change: the page is `INI_ECAM_ACTIVE_PAGE`, and a frame is used only when two frames in a row agree with that page (Task 5 test).
- GE and Rolls-Royce engines draw different engine blocks (N1 against EPR): the engine block's rows come from the labels drawn, not from a fixed list (Task 3 test with both fixtures, or the second recorded as not measured).
- Comma-decimal cultures: every row is the display's own characters (Task 3 and Task 5 tests under de-DE).

---

### Task 0 [controller]: Claim this plan

As plan 01's Task 0, with this file's path and `docs(a330): claim plan 08` (`Plan: 08`). Start from the branch once plan 02 has landed.

### Task 1 [sim]: The E/WD measured

**Files:** fixtures `tests/MSFSBlindAssist.Tests/A330/Fixtures/p08_ewd_*.json`; the evidence file.

- [ ] **Step 1:** Capture the E/WD (the reader program's `capture Ewd`) in these states, each with the owner's go-ahead where a control is moved, and put back after: on external power, engines off (reference: 18 texts, 64 characters; twelve amber XX engine fields, the fuel flow unit, fuel on board, the memos PARK BRK, ELEC EXT PWR, APU BLEED); with a caution (one hydraulic pump off, or a fuel pump off); with a red warning (the APU fire test); after T.O CONFIG; with the STATUS reminder; with both engines running (from plan 06's Task 6 or plan 11); on the other engine variant if the owner has it loaded.
- [ ] **Step 2:** From the captures, the E/WD's areas on its 780 x 780 display: where the engine block ends, where the left and the right message areas begin and their line pitch, which fonts and sizes each uses, and the colours seen. Write them, with the capture that shows each, in the evidence file. These become `A330EwdLayout`'s constants.

### Task 2: The E/WD's page model

**Files:** Create `Aircraft/A330/Ecam/A330EwdLayout.cs`, `A330EwdPage.cs`; test `tests/MSFSBlindAssist.Tests/A330/A330EwdPageTests.cs`.

**Interfaces:**
- Consumes: `A330Frame`, `A330Text`, `A330Colours` (plan 02).
- Produces:
  ```csharp
  namespace MSFSBlindAssist.Aircraft.A330.Ecam;
  internal sealed record A330EcamLine(string Text, string Colour, int Line);          // one message line; Colour is A330Colours.Name
  internal sealed record A330EwdPage(
      IReadOnlyList<(string Label, IReadOnlyList<string> Values)> EngineRows,         // a label the block draws and the values on its line, left to right
      IReadOnlyList<string> Unlabelled,                                               // engine-block texts with no label on their line, as drawn, with place
      string? FuelOnBoard, string? FuelFlowUnit,
      IReadOnlyList<A330EcamLine> Left, IReadOnlyList<A330EcamLine> Right)
  {
      /// Null unless the frame is the E/WD's and complete.
      public static A330EwdPage? From(A330Frame frame);
  }
  ```
  Texts on one baseline (within half a line pitch) in one message area and one colour, in x order, are one line, joined with single spaces.

- [ ] **Steps 1-5:** tests on Task 1's fixtures: `The_ground_fixture_has_three_green_memos_on_the_right_or_left_as_measured`, `A_caution_line_is_amber_and_in_the_left_area`, `A_warning_line_is_red`, `With_no_colour_no_colour_word_is_said`, `With_no_colour_no_alert_is_called_out`, `Fuel_on_board_and_its_unit_are_read`, `An_engine_field_showing_XX_is_kept_as_XX`, `Texts_of_one_line_are_joined_in_x_order`, `An_incomplete_frame_gives_no_page`; implement; commit `feat(a330): the E/WD's page model`.

### Task 3: The E/WD window

**Files:** Create `Aircraft/A330/Ecam/A330EwdText.cs`, `Forms/IniA330/A330EwdWindow.cs`; modify `Aircraft/IniA330Definition.Ecam.cs`; tests `A330EwdTextTests.cs`, `A330EwdWindowTests.cs`.

**Interfaces:**
- Consumes: `A330DisplayWindow`, `A330KeyRow` (plan 02); `HandleEcamHotkey`, `CloseEcamWindows` (plan 01).
- Produces: `internal static class A330EwdText { public static IReadOnlyList<string> Rows(A330EwdPage page); }`. Rows, top to bottom: one a labelled engine row ("N1: 21.0, 21.0"; "EGT: XX amber, XX amber" where a value's colour is not the normal one), the unlabelled texts as drawn, "FOB: 54080 KG", then "Left: PARK BRK, green" a line of the left area and "Right: ..." likewise, or "Left: none", "Right: none".

- [ ] **Steps 1-5:** tests: `The_ground_fixtures_rows_read_as_the_display` (the whole expected listing), `A_normal_coloured_value_says_no_colour_and_another_does`, `Rows_are_the_same_under_de_DE`, `Alt_E_opens_the_window_while_the_reader_runs`, `Alt_E_falls_back_to_the_AI_read_silently_when_it_does_not` (spec D3: the `ReadDisplayUpperECAM` AI read plan 01 moved here), `The_window_carries_the_ECAM_keys` (T.O CONFIG, CLR, RCL, STS, EMER CANC and ALL, by their keys in the map's ECAM panel), `Escape_returns_to_the_window_before`; implement `A330EwdWindow : A330DisplayWindow` (title "A330 E/WD", list "E/WD display", refresh 250 ms while shown); commit `feat(a330): the E/WD window`.

### Task 4: Warnings, cautions and memos called out

**Files:** Create `Aircraft/A330/Ecam/A330EcamAnnouncer.cs`; modify `Aircraft/A330/A330CalloutRows.Ecam.cs`, `Aircraft/IniA330Definition.Ecam.cs`; test `A330EcamAnnouncerTests.cs`.

**Interfaces:**
- Produces: `A330CalloutRows.Ecam` = three rows: `A330_CALLOUT_EWD_WARNINGS` "E/WD warnings", `A330_CALLOUT_EWD_CAUTIONS` "E/WD cautions", `A330_CALLOUT_EWD_MEMOS` "E/WD memos". `internal sealed class A330EcamAnnouncer { public IReadOnlyList<(string Text, bool Interrupt, string RowKey)> Next(A330EwdPage page, long nowTicks); public void Reset(); }` (pure).

- [ ] **Steps 1-5:** tests: `The_first_page_after_start_is_silent`, `A_new_red_line_is_a_warning_and_interrupts_once` ("ENG 1 FIRE, red"), `A_new_amber_line_is_a_caution_and_does_not_interrupt`, `A_new_green_or_white_line_is_a_memo`, `A_line_that_only_moved_is_not_new`, `A_line_gone_for_4_5_seconds_is_cleared` ("ENG 1 FIRE cleared"; warnings and cautions only), `A_line_that_returns_within_the_hold_says_nothing`, `Several_new_lines_of_one_kind_share_one_sentence`, `A_muted_row_says_nothing_but_keeps_tracking`, `A_flight_reload_starts_silent_again`; implement: while the A330 is active and any of the three rows is not muted, the definition wants `Ewd` at the background refresh plan 02's M1 set, feeds each complete frame that agrees with the one before it to the announcer, and speaks through the non-interrupting announce (the immediate one for an interrupt); commit `feat(a330): E/WD warnings, cautions and memos called out`.
- [ ] **Step 6:** The key other Airbus aircraft have for switching ECAM call-outs on and off (`ToggleECAMMonitoring`) toggles these three rows together and says the new state once; test `The_ECAM_call_out_key_mutes_and_unmutes_the_three_rows`.

### Task 5 [sim]: The lower ECAM: every page

**Files:** Create `Aircraft/A330/Ecam/A330SdPage.cs`, `A330SdLabels.cs`, `A330SdText.cs`, `Forms/IniA330/A330SdWindow.cs`; tests `A330SdPageTests.cs`, `A330SdTextTests.cs`, `A330SdWindowTests.cs`; fixtures `p08_sd_<page>.json`.

**Interfaces:**
- Produces:
  ```csharp
  internal enum A330SdPageId { Eng = 1, Hyd = 2, ElecAc = 3, ElecDc = 4, Bleed = 5, Cond = 6, Press = 7, Fuel = 8, Apu = 9, Fctl = 10, Door = 11, Wheel = 12, Status = 14 }
  internal sealed record A330SdPage(A330SdPageId? Page, string Title, IReadOnlyList<string> Lines, IReadOnlyList<string> Permanent)
  {
      /// Null unless the frame is the lower ECAM's and complete. `activePage` is L:INI_ECAM_ACTIVE_PAGE.
      public static A330SdPage? From(A330Frame frame, int activePage);
  }
  internal static class A330SdLabels
  {
      /// The name of a value the page draws without a label, by where it is drawn; null when none is established.
      public static string? NameAt(A330SdPageId page, float x, float baseline);
  }
  ```
  `Lines`: the page's texts grouped into screen lines top to bottom; on a line, a label and the values after it read "LABEL: value, value"; a value with a name from `A330SdLabels` reads "name: value"; any other text reads as drawn with its place. A value not in the normal colour says its colour. `Permanent`: the bottom band every page shares (temperatures, time, gross weight), read the same way.

- [ ] **Step 1:** Capture every page on the ground (press each ECAM page key through the simconnect MCP with the owner's go-ahead; the reader program's `capture Sd`; read `INI_ECAM_ACTIVE_PAGE` with it), DOOR/OXY with a door open and closed, STATUS with and without an inoperative system, and the pages that differ with engines running from plan 06's Task 6 or plan 11. In the evidence file, a page at a time: every text, its place, and what it is, with how that was established (a label on the display, a value changed by a known control, a variable that reads the same).
- [ ] **Steps 1-5 per page:** the page's fixture and its whole expected listing as a test (`A330SdTextTests`, one theory row a page); the label table's entries for that page, each proven by the fixture; under de-DE. Commit a page at a time: `feat(a330): the lower ECAM's <page> page`.
- [ ] **Step 2:** `A330SdWindow : A330DisplayWindow` (title "A330 system display", list "System display", refresh 250 ms while shown); its keys: one a system page (by the map's ECAM panel keys), STS, ALL, CLR and RCL; Alt+S opens it, or falls back to the AI read as Task 3's. Tests: `The_window_follows_the_page_shown`, `A_page_key_press_changes_the_list_without_a_word`, `A_frame_of_the_page_before_is_not_listed_under_the_new_title`.

### Task 6: The developer doc

- [ ] **Step 1:** "ECAM pages" rewritten: how the two displays are read, each area, the label tables and how an entry is proven, what is graphics and where its value comes from instead, the call-outs and their rows.

## In-sim checks (owner)

1. On the ground: Output Alt+E reads the engine block, fuel on board and the memos you expect; switch a hydraulic pump off: the caution is spoken once and listed; switch it on: "cleared" is spoken once.
2. Output Alt+S: press three page keys in the window; each page lists its values with names you recognise.
3. The APU fire test: the warning interrupts once, and its clear is spoken.

## Handoff to plan 12

- **Pilot-facing:** the two windows and their keys; the three Ctrl+M rows; the ECAM call-out key. Guide: the A330's chapter, a "Special windows" section; hotkey guide: Output Alt+E, Alt+S. Changelog `feature`.
- **Rule proposals:** INI-18: an E/WD or lower ECAM row carries the display's own text; a value is named only by a drawn label or a proven `A330SdLabels` entry, and anything else is listed as drawn with its place. INI-19: E/WD call-outs compare lines by text and colour between two agreeing complete frames; a clear is spoken after 4.5 s.
- **Frozen files:** the hotkey list form's text, if a new key needs a line.

## Execution record

Kept in this file only, so no other plan's push ever touches it.

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
