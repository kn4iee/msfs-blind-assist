# A330 MCDU Implementation Plan (plan 03, later)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** The MCDU window shows the page the aircraft drew, read through plan 02's reader: both sides, both font sizes, every special character named, in the app's Airbus MCDU format, with no pop-out, camera change or focus change. The scratchpad is confirmed from the MCDU's own text as it is typed, an FMS message is spoken with the window closed, the keys nobody verified are measured, and the pixel reader, its glyph file and its training tool are gone.

**Architecture:** A pure step turns a frame into the screen's 14 rows of 24 cells (character, small or large, colour), placing each text by where its first character sits on the grid. A second pure step turns the grid into the window's lines exactly as the form shows them today. The form keeps its keys, typing and layout and swaps its source: it asks the reader for the MCDU's frames while it is open. A small announcer watches the scratchpad row for messages while the window is closed.

**Tech Stack:** C# 13 / .NET 10 Windows Forms, xUnit; plan 02's `IA330Displays` and replay source; the reader program's `frame` and `capture` commands for the sim tasks.

**Spec:** `docs/a330/spec.md` sections 2.1, 3 (R6, R7), 5 (D3, D4, D5), 6 (M3, M4); `docs/ini-a330.md` "MCDU"; rules [INI-4] (retired by this plan), [INI-7].

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply. [INI-7]: never write `INI_MCDU1_CLR_KEY`.
- Owned files: `Aircraft/IniA330Definition.Mcdu.cs`, `Aircraft/A330/Mcdu/**` (new), `Aircraft/A330/A330CalloutRows.Mcdu.cs`, `Forms/IniA330/IniA330CduForm.cs`, `CduFormProfile.cs`; to delete: `Aircraft/A330/A330CduScreenReader.cs`, `A330CduPopout.cs`, `A330CduGlyphLibrary.cs`, `A330CduFrame.cs`, `CduLayout.cs`, `Resources/a330-cdu-glyphs.bin` and its one csproj line, `tools/a330-cdu-ocr/**`, `tests/MSFSBlindAssist.Tests/A330/A330CduScreenReaderTests.cs`; tests `tests/MSFSBlindAssist.Tests/A330/A330Mcdu*Tests.cs`; fixtures `p03_*.json`; `docs/ini-a330.md` "MCDU"; `docs/a330/evidence/03-mcdu.md` (new).
- The window shows a page only from a complete frame (`IA330Displays.Latest`). While there is none it keeps the last page for at most three refreshes, then shows the status row. It never shows half a page.
- What the window's lines look like, its keys and its typing pace do not change in this plan except where a task says so (Task 4b changes seven page keys to the Fenix's, spec D16): a pilot who knew the window finds it the same, only faster and without the camera moving. The one change in looks is the braille rule of "Measured on 2026-11-20": a row keeps its 24 columns, as the app's other Airbus MCDU windows do.
- The pixel reader is deleted only in Task 7, after the owner's check of Task 6. Until then both sources are in the tree and the form uses the new one.
- There is no AI read of the MCDU (spec D3): when the reader cannot read, the window shows why and BA speaks a message.
- Colours and hidden list rows follow plan 02's rule (spec M14, M15): build and test on the 2026-11-20 fixtures; at run time no colour means nothing is marked "(unavailable)", and `HiddenRowsLeftOut` false means no page rows and no FMS message are shown or spoken, only "List rows not read yet"; the window ships once M15 is solved or the owner rules otherwise.

## Measured on 2026-11-20

A whole preflight was typed on the captain's MCDU (KATL 26L VRSTY3 NOKIE, ALYNA4, ILS 17L KMCO, alternate
KTPA) and 88 MCDU frames were captured complete with the reader's first approach, each within 0.5 to 3 s. **Task 2 Step 1 (M3) and Task 4
Step 2 (M4) are done by this; the owner's steps remain.** The captured frames are with the private
reference; the fixtures this plan names are taken from them.

**Special characters (M3).** What the two fonts draw for each code:

| Code | Drawn | Seen on |
| --- | --- | --- |
| `o` | an entry box | INIT, PERF TAKE OFF, FIX INFO, NEW WAYPOINT |
| `d` | the degree sign | INIT (`---d`), IRS INIT, RAD NAV, HOLD, PERF |
| `a`, `b` | left and right arrows | beside a title (more pages), before a list row (`aATL2`), after a transition (`NOKIEb`) |
| `c` | the overfly mark | after a waypoint (`SNUFYc`) and alone on the scratchpad line |
| `e`, `f` | up and down arrows | bottom right of the F-PLN (the slew arrows) |
| `"` | ditto: same as the row above | F-PLN speed and altitude columns (small font) |
| `*`, `<`, `>`, `[`, `]`, `(`, `)`, `+`, `-`, `.`, `/`, `:`, `=`, `%` | themselves | everywhere |

**Keys (M4).** DECIMAL, SLASH and SPACE type their character. PLUS is the one +/- key: the first press
types `-`, a second press turns it into `+`. CLR deletes one character; on an empty scratchpad it writes
`CLR` (a line key then deletes that line's item: a hold, a discontinuity, a constraint, a reference fix)
and a second CLR removes it; with FMS messages queued each CLR removes one message. OVERFLY writes the
overfly mark; a left line key then marks the waypoint, and the same again clears it. The slew keys move
the F-PLN one waypoint a press and a DEPARTURES or ARRIVAL list four rows a press.

**What the pages do.**

- Lists are windows: the screen shows only some rows (four runways, four SIDs, three approaches, five
  F-PLN legs). With the reader's first approach the frame held only those. With its current one (spec
  D14) a frame can also hold the rows the MCDU does not show, drawn over the RETURN and scratchpad lines,
  until spec M15 is solved in the reader. A row the pilot cannot see must never be read as one they can,
  so: this plan builds and tests on the 2026-11-20 fixtures, which hold the screen's rows only (plan 02's
  rule for M14 and M15); at run time, while a frame's `HiddenRowsLeftOut` is false, the window shows no
  page rows and no FMS message (a hidden row could sit on the scratchpad line), only its status row
  "List rows not read yet"; and the MCDU window ships only once M15 is solved, or the owner rules
  otherwise (plan 12). (Claude's proposal.)
- A title can be several texts in two fonts and colours ("DEPARTURES" large, "FROM" small, "KATL" green;
  "HOLD", "AT", "RIGGZ"): the screen model joins them by column, as Task 1 does.
- The scratchpad line shows only the newest FMS message. "ENTRY OUT OF RANGE" was queued behind four
  amber messages and never shown until they were cleared, and an entry can be refused with no message at
  all (CRZ FL/TEMP `330/+52`). So after a line key the window says what the row now holds, or that it did
  not change: never silence (Task 4).
- Messages seen: SELECT DESIRED SYSTEM, WEIGHTS CLEARED, GPS PRIMARY, ENTRY OUT OF RANGE, AWY/WPT
  MISMATCH (white); ALIGN IRS, A/C POSITION INVALID, NAV ACCUR DOWNGRADED, NAV ACCURACY UPGRAD (amber;
  the aircraft's own spelling, as is the IRS MONTIOR page's title).
- **Slew direction: this window is the odd one out among the app's Airbus MCDU windows** (asked by the
  owner, 2026-11-20). The FlyByWire A320 and Fenix windows send key for key: Page Up (and Alt+Up on the
  FlyByWire) presses the MCDU's up-arrow key, Page Down its down-arrow key. This window swaps them: Page
  Down and Alt+Down press the aircraft's up-arrow key (`UARROW`), which moves on to later rows, and Page
  Up and Alt+Up press its `DOWN` key, so here "down" shows later rows as any list does. That was chosen
  for this aircraft on 2026-10-17 because its two keys looked named backwards; it is not a difference
  between the real A330 and the real A320, which share the MCDU (which arrow advances on the real
  aircraft was looked for and NOT found in a source: not verified). **The owner's ruling (2026-11-20): match the A320
  windows.** Done the same day in `CduFormProfile.A330` (Slew up = `UARROW` on Page Up and Alt+Up, Slew
  down = `DOWN` on Page Down and Alt+Down), pinned by
  `The_A330_window_slews_key_for_key_like_the_other_Airbus_windows`; this plan keeps that mapping when
  it rebuilds the form, and its Task 6 has the owner slew the F-PLN and a long list both ways.
- **The STAR list slews normally.** The owner slewed it with Alt+Up and Alt+Down in the pixel-read window
  (the same two MCDU slew keys as Page Up and Page Down) with an approach already chosen, and from where
  they left it (the fifth screen) the same two keys sent from outside the app moved it a screen at a
  time both ways. The earlier finding here, a list that stopped after its second screen, was a state the
  measuring itself had made: each of those tries had first slewed UP many times at the top of a list on
  that page. Not proven as the cause. For the window: never send the slew-up key when the list is
  already showing its first row, and Task 6 has the owner slew a long list to its end and back.
- Keys that do nothing on the ground in this aircraft: OFFSET and RTA (the prompts are drawn), EQUITIME
  POINT, CFDS (answers SELECT DESIRED SYSTEM); PREDICTIVE GPS shows "INOP PAGE".
- Entering the alternate shows another page for a moment: keys sent right behind it went astray. The
  window's typing pace stays as it is; never send a second entry before the page has settled.
- Pages captured: MCDU MENU, A/C STATUS, P/N XLOAD, INIT A and B (empty, filled, computed), IRS INIT,
  CLIMB and CRZ WIND, F-PLN (every screen of the route, with a hold, an overfly, a discontinuity, the
  alternate), LAT REV at the origin, a waypoint and the destination, DEPARTURES (runways, SIDs, a
  transition), ARRIVAL (approaches, STARs, transitions), HOLD (computed, edited), AIRWAYS, FIX INFO (a
  reference fix and a radius), VERT REV (climb and cruise waypoint, a constraint entered), STEP ALTS,
  DUPLICATE NAMES, DIR TO, PERF TAKE OFF, CLB, CRZ, DES, APPR, GO AROUND, PROG, REPORT, RAD NAV,
  SELECTED NAVAIDS, FUEL PRED, SEC INDEX, SEC INIT, DATA INDEX 1 and 2, POSITION MONITOR, IRS MONITOR, GPS
  MONITOR, CLOSEST AIRPORTS, PRINT FUNCTION, NEW WAYPOINT, ATSU MENU, AOC PRE-FLIGHT MENU, ACARS
  FUNCTIONS.
- Colours drawn: white, cyan, amber, two greens (F-PLN and values), yellow (a temporary flight plan),
  magenta (a constraint's star), orange (the take-off speed boxes), grey 0.30 (unavailable).

**Later the same day, programming the owner's real plan (a long-haul plan with an oceanic crossing).**

- **The route uplink is the INIT page's request prompt, shown only while FROM/TO is empty** (the owner's
  own preflight: the tablet's Dashboard import first, then this prompt, then on INIT B 1R twice for the
  zero fuel weight and 3R twice for fuel planning). It filled INIT A (a CO RTE named for the two airports, alternate,
  flight number, cost index, FL390) and a 100-row flight plan with every airway named on its legs, the
  ocean coordinates, the STAR, the approach with its transition, the missed approach and the alternate.
  Task 6 walks exactly this sequence in the window. Not the uplink: ACARS FUNCTIONS' flight plan request
  shows "INOP PAGE", and on the AOC PRE-FLIGHT MENU only OOOI STATUS opens a page.
- A value key pressed twice is how this MCDU takes its own figure: the first press of 1R on INIT B puts
  the aircraft's zero fuel weight and CG on the scratchpad, the second enters it. The window says what
  arrived on the scratchpad after the first press.
- The AIRPORT key is not a page: it moves the flight plan to the destination, then the alternate, then
  back to the origin. The window's AIRPORT button says which of the three it reached.
- Entering FROM/TO for a city pair the aircraft knows opens a route page (titled with the two airports and `1/1`, a stored
  route with RETURN and INSERT); entering an alternate opens a page titled with the destination and the alternate, with NONE and RETURN. The INIT
  page takes no entry until RETURN is pressed: the window must show these pages as what they are, and
  the pilot's next typed entry must not be lost on them.
- A waypoint typed into the F-PLN makes a TEMPORARY plan: the title reads `FROM TMPY`, the last line
  `*ERASE` and `INSERT*`, and until INSERT the next entry answers TEMPORARY F-PLN EXISTS. The AIRWAYS
  page takes several VIA and TO pairs and then shows `TMPY <F-PLN` and `TMPY INSERT*`. The window says
  "temporary flight plan" when the title carries TMPY, so a pilot knows INSERT is waiting.
- Changing FROM/TO or the runway queues CHECK TAKEOFF DATA and CHECK T.O DATA.
- `5050N` is taken as 50N 050W. A coordinate that arrived from the tablet showed as a waypoint row with
  a BLANK name (its position only on its LAT REV page): a row with no name is read as "unnamed
  waypoint", never skipped.
- CLR arms a delete only with an empty message queue: with a message waiting, CLR removed the message
  and the line key then opened LAT REV. Deleting a waypoint or a hold leaves a DISCONTINUITY.
- **Keys can arrive out of order.** A five-character flight number sent at 0.2 s a key came out with its first two letters swapped once, with the simulator
  running slowly: two key flags set before the aircraft read them are taken in its own order. Task 4:
  after typing, the window compares the MCDU's own scratchpad text (plan 02 gives it at once) with what
  was typed before it sends the line key, and says so when they differ; it never sends a second
  character while the first has not shown.
- The aircraft takes the simulator's own flight plan when a flight starts: after a reload the FMS held
  FROM/TO, the cruise level, the departure, every waypoint and the STAR from the simulator's Flight
  Planner, with no flight number, cost index or alternate.
- ARRIVAL: NEXT and PREV switch between the approach list and the STAR list (the arrows beside the
  title). A list keeps its scroll position between visits: one slewed past its end comes up EMPTY, and
  the key that slews toward the top brings it back. Approaches are three to a screen (`ILSZ25R` with its
  length, course and ILS identifier and frequency under it). The aircraft draws all 40 STARs and shows
  three: the window could list every row of such a list (the reader could give the rows outside the
  window as well, marked as not on screen), which would save slewing through thirteen screens: a
  candidate for the owner, since it is more than a sighted pilot sees.
- A 100-leg flight plan is five rows a screen: reading it took 20 screens. A "whole flight plan"
  read-out in the window (slewing for the pilot) is a candidate for the owner, not part of this plan.

**Braille and the other aircraft's windows (a requirement of Task 3, not only a check in Task 6).** The
pixel-read window squeezes every gap of three or more spaces to three, so a row's right-hand field moves
from row to row; the FlyByWire A32NX window (`FbwMcduFormat.PositionLine`) and the 757's ([B757-56]) keep
every field in its column because a braille reader finds a field by its column. This aircraft is the odd
one out, and the screen model now has the true 24 columns, so:

- a data row is its 24 columns as drawn, right-trimmed, after the row's prefix; the gap between the left
  and the right field is never squeezed;
- every special character shows as ONE cell (the box, the degree sign, each arrow, the overfly mark, the
  ditto), with the stand-ins the Fenix and FlyByWire windows use, so no later column moves;
- a marker the app adds (the "(unavailable)" of a greyed prompt) never moves a later column and never
  takes a row past the 40 cells of a braille line with its prefix: it goes after the row's last
  character, or is shortened, as [B757-56] does for its marker;
- one list item a screen row, the status row reachable by Tab, the scratchpad row always present.

Tests to add to Task 3: `A_right_hand_field_keeps_its_column_on_every_row`,
`Each_special_character_takes_one_cell`, `The_unavailable_marker_moves_no_column`,
`No_row_with_its_prefix_passes_40_cells`.

## Review Focus

- A page key that silently does something else: a button label's `&` letter takes Alt+letter in WinForms, so a label left as `&PERF` would steal PROG's new Alt+P. Task 4b's test checks every label's letter against its shortcut, and that no two keys share one.

- A row whose two texts overlap a column (a label drawn over a value) must not lose a character: the later-drawn text wins the cell, and the test page has such a row (Task 1).
- A centred label sits between two columns (measured 2026-11-20: 33 of 2,040 texts start 0.35 to 0.5 of a cell off a column, among them "AVAILABLE RUNWAYS", "VIA", "UTC", "HOLD AT", "INOP PAGE" and the REPORT page's times; one text, the "1/7" of P/N XLOAD, sits half a row off). Such a text takes the nearer cell: it is never a reason to refuse the page. The screen is refused only when a text starts outside the 14 by 24 area, which is what a moved geometry (another variant, another gauge size) gives (Task 1).
- An amber box row read as letters ("oooooooo"): every special character has its name before any page is shown (Task 2).
- A message on the scratchpad line mistaken for typed text: the typed text is the MCDU's own typed line, which the reader sends apart from the page; the row is what is displayed (Task 4).
- CLR pressed to delete a character while a message is queued clears the message instead: Backspace says what was cleared (Task 5).
- Comma-decimal cultures: every line the window builds is the aircraft's own characters, never a number formatted by the app (Task 3 test under de-DE).

---

### Task 0 [controller]: Claim this plan

As plan 01's Task 0, with this file's path and `docs(a330): claim plan 03` (`Plan: 03`). Start from the branch once plan 02 has landed.

### Task 1: The screen from a frame

**Files:** Create `Aircraft/A330/Mcdu/A330McduScreen.cs`, `A330McduGeometry.cs`; test `tests/MSFSBlindAssist.Tests/A330/A330McduScreenTests.cs`; fixtures `p03_init.json`, `p03_menu.json`, `p03_fpln.json` (captured with the reader program's `capture` command).

**Interfaces:**
- Consumes: `A330Frame`, `A330Text`, `A330Rgb` (plan 02).
- Produces:
  ```csharp
  namespace MSFSBlindAssist.Aircraft.A330.Mcdu;
  internal static class A330McduGeometry
  {
      public const int Rows = 14, Columns = 24;
      // Measured on the 450 x 400 display: a text's first character sits at Left + column * ColumnPitch, its baseline at Top + row * RowPitch.
      public const float Left = 22f, ColumnPitch = 16.87f, Top = 43f, RowPitch = 25.85f;
      public const string LargeFont = "FMGSfont_A", SmallFont = "FMGSfont_B";
  }
  internal readonly record struct A330McduCell(char Character, bool Small, A330Rgb? Colour);   // null while the reader gives no colour (M14)
  internal sealed class A330McduScreen
  {
      /// Null when the frame is not complete or not drawn, or a text's first character rounds to a cell outside the 14 rows and 24 columns.
      public static A330McduScreen? From(A330Frame frame);
      public A330McduCell? this[int row, int column] { get; }       // null for an empty cell
      public string Text(int row);                                   // the row's characters, spaces for empty cells, right-trimmed
  }
  ```
  A text's first character goes to the nearest row and column (half-way rounds to the lower one), and its character k to that column + k (a text's characters advance by whole pixels and drift from the grid, so only the start is placed by position). Characters that would pass column 23 are dropped, not a reason to refuse. Texts are placed in their drawing order (`A330Text.Order`), so a later text overwrites an earlier one's cells. A space leaves an empty cell.

- [x] **Steps 1-5:** tests: `The_INIT_fixture_fills_the_grid_as_the_cockpit_shows_it` (the fixture's 12 rows: row 0 `          INIT        ab`, row 1 ` CO RTE          FROM/TO`, ..., row 12 `---- /---d         36090`), `Labels_are_small_and_data_large`, `A_later_text_wins_a_shared_cell`, `A_centred_label_between_two_columns_takes_the_nearer_one` (the DEPARTURES fixture's "AVAILABLE RUNWAYS" at column 3.44), `A_text_half_a_row_off_takes_the_nearer_row`, `A_text_outside_the_screen_refuses_it`, `No_fixture_page_has_two_texts_claiming_one_cell_with_different_characters` (true of all 88 captured pages), `An_incomplete_frame_gives_no_screen`, `The_first_officers_frame_uses_the_same_geometry`; implement; commit `feat(a330): the MCDU screen from a frame`.

### Task 2 [sim]: Every special character named (M3)

**Files:** Create `Aircraft/A330/Mcdu/A330McduCharacters.cs`; test `A330McduCharactersTests.cs`; the evidence file.

**Interfaces:**
- Produces: `internal static class A330McduCharacters { public static string Display(A330McduCell cell); public static bool IsBox(char c); }`: what the window shows for a cell's character (the character itself, or the special character's stand-in).

- [x] **Step 1:** List every character the two MCDU fonts can draw and which are not what their code says. Sources, in this order: the pages that use them, read with the reader program's `frame` command (INIT: `o` boxes, `d` in "---d", `a` `b` beside the title; F-PLN, PROG, RAD NAV, PERF, DIR, the airways page, a hold: brackets, the overfly triangle, up and down arrows, the ditto mark); the labels in `tools/a330-cdu-ocr/data/big/truth.json`; for a character whose shape must be known, the private reference's notes. Record the table with each character's source in the evidence file.
- [x] **Step 2:** Steps 1-5 for the table: tests `A_box_shows_as_the_apps_box_character` (the same stand-in the Fenix MCDU window uses, [A320-31]), `The_degree_sign_and_the_arrows_show_as_themselves`, `An_unknown_code_shows_as_itself`; commit `feat(a330): the MCDU's special characters`.
- [ ] **Step 3 [owner]:** The owner hears a page with boxes, a degree value and the page arrows, and rules on each stand-in's wording.

### Task 3: The window's lines from the screen

**Files:** Create `Aircraft/A330/Mcdu/A330McduLines.cs`; modify `Forms/IniA330/CduFormProfile.cs` (`CduLineText`); test `A330McduLinesTests.cs`.

**Interfaces:**
- Produces:
  ```csharp
  /// One screen row as the window's source: the same shape the form consumed from the pixel reader.
  internal sealed record A330McduLine(int Row, string Text, IReadOnlyList<A330McduCell?> Cells);
  internal static class A330McduLines
  {
      public static IReadOnlyList<A330McduLine> From(A330McduScreen screen);   // 14 lines, row 0 first
      public const int ScratchpadRow = 13;
  }
  ```
  The form's existing line builder (title, each label row indented without a number, each data row "n: value", "Scratchpad: ...") stays and takes these lines. Colour (spec D4): no colour word is spoken or shown. A greyed prompt keeps "(unavailable)": a cell is grey when its red, green and blue are equal and below 0.5 (the MCDU MENU's unselectable prompts measured 0.30). With no colour (M14) nothing is marked "(unavailable)" and the status row says "Colours not read yet" (test `Without_colours_no_prompt_is_marked_unavailable`). A data row made only of boxes reads as the app's box characters. The small font carries no words: labels are already told apart by their row.

- [x] **Steps 1-5:** tests on the three fixtures: `INIT_reads_as_title_labels_numbered_data_and_scratchpad` (the whole expected listing in the test), `MCDU_MENU_marks_the_greyed_prompts_unavailable`, `A_row_with_left_and_right_fields_keeps_both`, `An_empty_scratchpad_reads_empty`, `No_line_carries_a_colour_word`, `The_listing_is_the_same_under_de_DE`; implement; commit `feat(a330): the MCDU window's lines from the screen`.

### Task 4: The form on the reader, both sides

**Files:** Modify `Forms/IniA330/IniA330CduForm.cs`, `CduFormProfile.cs`, `Aircraft/IniA330Definition.Mcdu.cs`; test `A330McduFormTests.cs`.

**Interfaces:**
- Consumes: `IA330Displays.Want/Release/Latest/McduScratchpad/StatusText/State`, plan 01's `HandleMcduHotkey`, `CloseMcduWindows`.
- Produces: `CduFormProfile` loses `Popout` and `CreateReader` and gains `A330Gauge Gauge` and `bool FirstOfficer`; `CduFormProfile.A330(bool firstOfficer)` (key prefix `INI_MCDU1_` or `INI_MCDU2_`).

- [x] **Step 1:** Steps 1-5: tests `The_form_wants_its_gauge_while_visible_and_releases_it_when_hidden`, `A_new_complete_frame_updates_the_list_in_place`, `No_complete_frame_for_three_refreshes_shows_the_status_row`, `Opening_the_window_while_the_reader_cannot_read_speaks_its_status_once` (spec D3: "A330 MCDU unavailable: " and the reason, from `A330ReaderStatus`; never an AI read), `The_status_is_spoken_again_only_when_the_reason_changes`, `When_the_reader_starts_reading_again_the_window_says_MCDU_ready_once`, `Keys_are_still_sent_while_the_page_cannot_be_read_and_a_typed_key_is_said_as_not_confirmed`, `The_title_is_announced_when_it_changes_and_the_selection_returns_to_line_1`, `Typed_text_is_confirmed_from_the_MCDUs_own_scratchpad` (after each key the form waits for `McduScratchpad` to end with the key's character, up to 600 ms, then says the scratchpad; a key that never lands is said as missed), `Ctrl_Shift_R_and_Ctrl_Shift_L_switch_sides` (the app's `SideShortcut` convention); implement: the 250 ms poll becomes `Want(gauge, this, 250)` plus a 250 ms UI timer reading `Latest`; the pop-out, the camera reads and writes and the foreground calls are no longer called from the form.
- [x] **Step 2 [sim] (M4):** Through the window, with the owner's go-ahead: OVERFLY on a flight-plan waypoint, DECIMAL, SPACE, PLUS/MINUS, SLASH, and CLR three ways (a character, a queued message, an empty scratchpad). Record what each did. Fix a wrong key name in `CduFormProfile` test-first.
- [x] **Step 3:** Whether the reader's typed line holds a displayed message or only typed text (type one character with a message queued, read both): record it; the typed-text confirmation of Step 1 follows what was measured.

### Task 4b: The page keys as the Fenix's (parity, spec D16)

The owner asked on 2026-11-21 for the A330 window's keys to be brought up to parity with the Fenix's. The FlyByWire A320 window already matches the Fenix on purpose ("so the muscle memory transfers"), and the FlyByWire A380 window uses Alt+E for PERF too; the A330 is the one that differs. Seven page keys change; every other key already matches (Alt+D DIR, Alt+I INIT, Alt+F F-PLN, Alt+R RAD NAV, Alt+U FUEL PRED, Alt+M MCDU MENU, Alt+Left and Alt+Right to page, Page Up and Page Down or Alt+Up and Alt+Down to slew, Ctrl+1 to 6 and Alt+1 to 6 or F1 to F12 for the line keys, Alt+S the scratchpad, Alt+Home the display, Backspace CLR).

| MCDU key | Today | After (the Fenix's) |
| --- | --- | --- |
| PERF | Alt+P | Alt+E |
| PROG | Alt+G | Alt+P |
| DATA | Alt+A | Alt+T |
| AIRPORT | Alt+O | Alt+A |
| OVFY | Alt+V | Alt+O |
| ATC COMM | Alt+T | Alt+K |
| SEC F-PLN | Alt+X | Alt+Shift+F |

**Files:** Modify `Forms/IniA330/CduFormProfile.cs` (`PageKeys` labels and tips, `AltKeys`), `Forms/IniA330/IniA330CduForm.cs` (Alt+Shift+F); test `tests/MSFSBlindAssist.Tests/A330/A330McduKeysTests.cs` (new).

**Interfaces:**
- Consumes: `FenixMCDUForm`'s and `FlyByWireMCDUForm`'s page buttons, as the yardstick only (no shared code is introduced).
- Produces: `CduFormProfile.A330`'s `PageKeys` and `AltKeys` with the table's shortcuts; button labels whose `&` letter is the shortcut's letter (WinForms takes Alt+letter from a button's `&`, so `&PERF` would take PROG's Alt+P): `P&ERF`, `&PROG`, `DA&TA`, `&AIRPORT`, `&OVFY`; ATC COMM and SEC F-PLN carry no `&` (Alt+K through `AltKeys`, Alt+Shift+F in the form's key handler, as the Fenix and FlyByWire windows do).

- [x] **Steps 1-5:** tests: `The_page_keys_match_the_Fenix_MCDU_window` (a theory over all thirteen page keys and their shortcuts), `No_two_page_keys_share_a_shortcut`, `Every_labels_letter_is_its_shortcuts_letter`, `Alt_Shift_F_presses_SEC_F_PLN`, `The_tips_name_the_new_shortcuts`; implement; commit `feat(a330): the MCDU window's page keys match the Fenix's` (`Plan: 03`).

### Task 5: FMS messages, spoken with the window closed

**Files:** Create `Aircraft/A330/Mcdu/A330McduMessages.cs`; modify `Aircraft/A330/A330CalloutRows.Mcdu.cs`, `Aircraft/IniA330Definition.Mcdu.cs`; test `A330McduMessagesTests.cs`.

**Interfaces:**
- Produces: `A330CalloutRows.Mcdu` = one row, key `A330_CALLOUT_FMS_MESSAGE`, label "FMS messages". `internal sealed class A330McduMessages { public string? Next(A330McduScreen screen, string? typedScratchpad); }` (pure: the message to speak, or null).

- [x] **Steps 1-5:** tests: `A_new_message_on_the_scratchpad_row_is_spoken_once` ("MCDU message: INITIALIZE WEIGHT/CG"), `Typed_text_is_never_a_message` (the row equals the reader's typed line), `No_message_is_taken_while_hidden_rows_are_not_left_out`, `The_first_reading_after_start_is_silent`, `A_message_that_returns_after_being_cleared_is_spoken_again`, `A_muted_row_says_nothing`, `Backspace_on_a_queued_message_says_message_cleared` (the form, CORE-7: once); implement: while the A330 is active and the row is not muted the definition wants `McduCaptain` at the slow refresh plan 02's M1 set for a background watch, speaks through the non-interrupting announce, and stops watching when muted or switched away; commit `feat(a330): FMS messages with the window closed`.
- [x] **Step 2 (review, 2026-11-22):** while the reader's frames cannot yet say the message (hidden rows, M15), the aircraft's FMS message lamps (`INI_FMS1_message_light`, `INI_FMS2_message_light`, found by name; measure what they read with a queued message) say a message is waiting, in the Fenix's MCDU FM lamp sentence, under the same row, the first reading silent, and stop once the frame can say the message itself. Test `The_FMS_message_lamp_says_a_message_is_waiting_once`. Commit.

### Task 6 [sim] [owner]: The window against the cockpit

- [x] **Step 1:** With the reader program's `frame` command beside it, walk these pages on both sides and compare the window's lines with the frame's rows: MCDU MENU, INIT A and B, DATA INDEX 1 and 2, A/C STATUS, and after the owner's route is loaded: F-PLN (three slews), a LAT REV, a DEPARTURE runway and SID list, PERF TAKE OFF, PROG, RAD NAV, FUEL PRED. Capture each as a `p03_<page>.json` fixture with its expected listing as a test (`A330McduPagesTests`, one theory row a page).
- [x] **Step 2:** The window's formatting against the app's other Airbus MCDU windows and on a braille display (one list item a screen row, no wasted cells, the status row reachable): list every difference in the evidence file as a candidate for the owner; change nothing unasked.
- [ ] **Step 3 [owner]:** The owner programs a route in the window with the pop-out never appearing and the camera never moving, and says whether anything reads worse than before.

### Task 7: Remove the pixel reader

**Prerequisite:** Task 6 Step 3 passed. Otherwise leave this task and record why.

- [ ] **Step 1:** Delete the files listed under "to delete" in Global Constraints and the `a330-cdu-glyphs.bin` line in `MSFSBlindAssist/MSFSBlindAssist.csproj` (the index's named exception). Remove what only they used from `CduFormProfile.cs` and `IniA330Definition.Mcdu.cs` (`CduPopoutProfile`, `CduLayout`, `A330CduGlyphLibrary`, the camera restore).
- [ ] **Step 2:** `git grep -n -i -E "a330-cdu-ocr|A330CduScreenReader|A330CduPopout|CduPopoutProfile|a330-cdu-glyphs"` prints only docs and rules that plan 12 owns (list them in the handoff). Build, the whole suite, `ClaudeContextBudgetTests` green; commit `refactor(a330): remove the pixel-read MCDU`.

## In-sim checks (owner)

1. Shift+M with no pop-out open: the page is read within a second (after "A330 displays starting", the first time in a simulator session); the camera and the simulator's window do not move.
2. Type a flight number and a FROM/TO pair: each character is confirmed as it lands; a wrong entry's message is spoken.
3. Close the window, cause a message (enter an invalid value before closing, or wait for one): it is spoken once.
4. Ctrl+Shift+R: the first officer's MCDU, on its own page.
5. The page keys as on the Fenix: Alt+E PERF, Alt+P PROG, Alt+T DATA, Alt+A AIRPORT (a jump), Alt+O OVFY, Alt+K ATC COMM, Alt+Shift+F SEC F-PLN.

## Handoff to plan 12

- **Pilot-facing:** the MCDU window needs no pop-out, works from any view, reads both sides; FMS messages are spoken; the "FMS messages" Ctrl+M row; the page keys now match the Fenix and FlyByWire A320 windows (Task 4b's table: a pilot who used the A330 window before has seven keys to relearn, which the changelog says). Guide: the A330's MCDU section and "Things to know" (the pop-out text goes; the key list). Hotkey guide: Ctrl+Shift+L/R and the MCDU key list (today's `HotkeyGuides/iniBuilds_A330_Hotkeys.txt` lists the old keys). Changelog `improvement`.
- **Pilot-facing, added while working (for the guide and the changelog):** a Left/Right side selector in the window (as the Fenix's); the status row is a read-only box, first in the tab order; the scratchpad line now reads "Scratchpad: ..."; a greyed prompt reads "(unavailable)", or "(inop)" where only that fits before the next field; what BA says: "A330 MCDU unavailable: <reason>" on opening while the reader cannot read, "MCDU ready" once it reads again, the typed text read back once it shows on the MCDU, "Missed <KEY>. Scratchpad: <text>" when a key does not show (the entry stops), "Typed <text>, not confirmed" when nothing confirms it (since 2026-11-23 nothing for a line key, a slew, AIRPORT or Backspace, and no TMPY phrase: see the execution record), the FMS message alone with the window closed (as the A32NX), and the lamp sentences "MCDU FM Captain: On/Off" and "MCDU FM FO: On/Off" (the Fenix's).
- **Rule proposals:** [INI-4] retired ("the pixel reader it guarded was replaced"), once Task 7 has run; INI-13: the MCDU window shows a page only from a complete frame and places a text by its first character's cell, refusing a frame whose text starts off the grid; when the reader cannot read it speaks the status once and never falls back to an AI read. INI-14 (new): the MCDU window keeps a row's 24 columns (`CduLineText.ForWindow`): a special code is one cell (`A330McduCharacters`), a greyed-prompt marker never moves a later column and no row passes 37 cells (40 with its prefix); pinned by `A330McduLinesTests`. INI-15 (new): an MCDU entry is confirmed from the MCDU's own typed line, key by key (`IniA330CduForm.TypeAsync`), never from the page's scratchpad row while the reader gives the typed line (the row can hold a message or a hidden list row); with no typed line, the row's read-back is the confirmation; pinned by `A330McduFormTests`.
- **Frozen files:** the csproj line (done here by exception, in Task 7); `docs/development.md` and CLAUDE.md's tool count (`tools/a330-cdu-ocr/harness` is gone, in Task 7); the rule file's `tools/a330-cdu-ocr/**` glob (Task 7). None asked for by Tasks 1 to 6.
- **For plan 12 and the owner:** the release must carry the display reader built from its current private branch (2026-11-22 or later): an older one sends no `hiddenRowsLeftOut`, and the window then lists only "Reading the captain's MCDU" on every page. Pilot-facing additions for the guide and the changelog: `Title: ` on the window's first line, a slew key with nowhere to go does nothing (silently, and the list never sticks), `unnamed` rows, the new side's page said after Ctrl+Shift+L/R. A frame the reader cannot vouch for keeps the page shown for up to 3 s, then shows "Reading the captain's MCDU" (expected now and then, for example when both MCDUs show pages with a similar number of texts). The candidates the window's formatting raises against the Fenix and FlyByWire windows, and what the plan describes but no task built, are in the evidence file's "Task 6 Step 2". [INI-4] and `docs/ini-a330.md`'s two camera bullets still describe the pixel reader's pop-out until Task 7. `Aircraft/IniA330Definition.cs` (plan 01's file) still says the MCDU is read from the pixels of a forced pop-out; `docs/ini-a330.md`'s MCDU bullet must be checked for anything that points into `docs/a330/` before that folder is deleted.

## Execution record

Kept in this file only, so no other plan's push ever touches it.

- **Status:** waiting on owner (the window's in-sim check with the display reader's build of 2026-11-22, which solves M14 and M15 for the MCDU; Task 2 Step 3; Task 6 Step 3; then Task 7)
- **Claimed by:** kn4iee
- **Worked by:** kn4iee, with Claude (subagent-driven development: an implementer and a review a task, a whole-plan review)
- **Started:** 2026-11-22, from d191e9684 (the branch tip, plan 02 landed)
- **Landed:** not yet: Tasks 1 to 6 Step 2 are on the branch (last code commit 7b7a1f5c5 before the whole-plan review); Task 7 waits
- **Tests at landing:** 26,176 passed, 0 failed (filter `FullyQualifiedName!~ClaudeRulesHookTests`; 26,056 at the start), on c106b2d88 after the whole-plan review (Opus: no Critical; six Important, all fixed in b66da5d8a and c106b2d88, the fix wave re-reviewed clean)
- **Tasks:** 0 claim 56319730f (pushed first). 1 the screen c5286bc8c. 2 the characters f4feb028b, review fix a5a77169d (Step 1's table by the controller, in the evidence file). 3 the lines b53aeefa4, fix d9d92807f. 4 the form on the reader d05fe0b1f, fix 7ca9ad135 (Step 2 done by the 2026-11-20 measurement; Step 3 measured 2026-11-22). 4b the page keys 544f6e581. 5 FMS messages 2e5e45e62 and the lamps 78d351530, fix 9559ab62b. 6 Step 1 offline: the pages test e3fb55035, fix 7b7a1f5c5; Step 2 in the evidence file. Each task had its own review (spec and quality) and every push ran the private push check first.
- **Waiting on the owner:** Task 2 Step 3 (the stand-ins' wording, the box `-` above all: evidence "Task 6 Step 2" item 2); Task 6 Step 3 (programming a route in the window); Task 7 after it; the five in-sim checks above, now with the display reader's build of 2026-11-22 beside the app (it leaves hidden list rows out); a list slewed to its top and end (silent, never stuck), and pages changed and scrolled with no "Reading" flash (the build of 2026-11-23); what the FMS lamps read with an amber message.
- **In-sim results:** 2026-11-22, MSFS 2024 1.9.24.0, A330-200 (GE), in flight, reader 1.0.0: reads of both MCDUs and the lamps; on the first officer's MCDU only, keys through the calculator path (DATA, X, SPACE, LSK4R, INIT, X, LSK5L, Y, CLR), put back as found. Results in the evidence file: a trailing space stays on the typed line; a displayed message is never on the typed line; typing replaces a white message; the lamp stays 0 for a white message; the first officer's frames use the captain's grid; today's frames carry no colour and no `hiddenRowsLeftOut`.
- **Changed from the plan:**
  - Fixtures: the 2026-11-20 captures' frame blocks with `"hiddenRowsLeftOut": true` added (those frames held the screen's rows only), from Claude's own test route only: `p03_init`, `p03_menu`, `p03_fpln`, `p03_departures`, `p03_xload` (Tasks 1 to 3) and thirteen more for Task 6 (`p03_initb`, `p03_data1`, `p03_data2`, `p03_status`, `p03_fpln1` to `3`, `p03_latrev`, `p03_sids`, `p03_perfto`, `p03_prog`, `p03_radnav`, `p03_fuelpred`).
  - "Every one of the 88 pages" (Task 1): the test covers the committed fixtures; the 142 captured MCDU frames were checked once by the controller (evidence).
  - Added: `A330McduScreen.HiddenRowsLeftOut`; `CduLineText.Listing` and `ForWindow`; the status rows in `A330McduStatus` (the words of `A330DisplayWindow`, which the form does not derive from: it keeps its own layout and speaks); `A330McduAvailability`, `A330McduLineKeyWatch`; the moved key tests in `A330McduKeysTests`; the `+` typed as two PLUS presses; the line-key read-back and the TMPY phrase of "Measured on 2026-11-20"; the side selector; the FMS lamp variables as the message watch's start.
  - Task 6 Step 1 ran offline on the captured frames against a second implementation of the rules (all eighteen pages agreed); the owner's walk is Step 3.
  - After the whole-plan review, at the owner's word ("However this is formatted in other mcdus. Fix all and do what needs to be done"): the window's formatting follows the Fenix and FlyByWire windows where they agree (`Title: ` on the first line, a page counter drawn half a row under the title put on it, the new side's page said after a side switch, "No change" only when nothing on the page changed), the box stays the Fenix's `-`, and three things the plan describes were built (AIRPORT says the waypoint it moved to, a nameless F-PLN row reads `unnamed`, and the slew guard below) (9829dbbf1).
  - The display reader's own work (its private plan, outside this repository) now gives the MCDU's colours and leaves its hidden list rows out, live on 2026-11-22 (the first officer's F-PLN, and DEPARTURES at EDDF scrolled and not). With it the frame says whether a list has rows hidden above or below: `A330Frame` gains `List`, `ListAbove`, `ListBelow` and `A330ReaderProtocol` reads them (plan 02's files, landed and held by no open plan: changed here for this fix), and the window never slews past a list's top or end ("Top of list", "End of list"), the owner's report of a stuck ARRIVAL list (7f62a7b49, 638224599).
  - Task 5's message sentence is the A32NX's (the message alone, pinned against `FbwMcduReadBack`), not the "MCDU message: ..." of Task 5's text: the index's announcement convention (owner, 2026-11-22) came after the plan; the evidence lists both. A list page whose hidden rows fall off the grid lists "List rows not read yet", as plan 02's window does, never "cannot read" (worded "Reading the captain's MCDU" since 2026-11-23, below).
  - 2026-11-23, the owner on the build with the new reader: "These whole 'list announcements' it's just too much! None of our other planes have any of this, they scroll without announcements, also it says 'list rows not readable' even during refreshes and as pages are loading. The user side needs to be like the fenix and other mcdu readers, forms, and announcements!", then "The user doesn't need to know that they're at the beginning or end either!". Cause of the flashes: the reader sends every new frame (a page change, a slew, a clock that ticks) once before it has left the hidden rows out, and the window swapped the page for its status row for that frame. Now: the window keeps the page shown while such frames come, for up to 3 s after the last page read (`HoldPageMs`), then lists "Reading the <display>", the display windows' words; a slew is judged only on a page (waiting up to 1.5 s, `SlewShowsWithinMs`, was 1 s), which also closes a gap where a slew pressed during that frame went unguarded. Removed, as no other MCDU window (Fenix, FlyByWire A320, PMDG 737, MD-11) says them: "Top of list" / "End of list" (the guard stays, silent), the line-key read-back and "No change" (`A330McduLineKeyWatch` deleted), the AIRPORT key's waypoint (`A330McduAirportWatch` deleted), "Message cleared: ..." (the scratchpad line's read-back says "Scratchpad cleared" or the next message, as on the others), ", temporary flight plan" and "Temporary flight plan" (the title is said as drawn, TMPY in it), and "Colours not read yet" on the status box. The ruling "After a line key" below is withdrawn.
- **Rulings made while working** (Claude's; the owner may reverse any):
  - Stand-ins: box `-` (the Fenix's), degree `°`, arrows `← → ↑ ↓`, overfly `Δ` (the Fenix's), ditto `"`; one cell each.
  - A space in a text writes nothing; a text half-way between two cells takes the lower.
  - Greyed prompts: one marker a run of greyed fields, "(unavailable)" in the gap when it fits with two blanks kept, else "(inop)", else none; at the row's end "(unavailable)", without its blank when the row would pass 37 cells.
  - The title row trimmed at both ends, no gap squeezed; "Scratchpad: " before the scratchpad line (the Fenix and FlyByWire windows').
  - Status row words as `A330DisplayWindow`'s; "A330 MCDU unavailable: <reason>" only for a lasting state or the 30 s "cannot read"; "MCDU ready" once after it.
  - Typing waits for the MCDU's typed line, 600 ms a key, the 150 ms pace kept; a missed key stops the entry; an entry stops when the window is hidden, closed or switches side.
  - After a line key: the changed row, or "No change" after 1.5 s; nothing with no page shown. (Withdrawn 2026-11-23, owner: a line key says nothing of its own.)
  - Sides as the Fenix window: Left and Right, Ctrl+Shift+L/R.
  - FMS messages: the captain's MCDU only, read every second while the A330 is active and the row is not muted, silent while the MCDU window is open; a message needs two frames in a row (one frame never counts twice); the F-PLN's slew arrows on that line are not a message; text that is the typed line or a beginning of it is not a message.
  - The FMS lamps: 0 off, anything else lit; the Fenix's sentences; the captain's lamp only while the frames cannot say the message; a flight load keeps the lamp baselines (SimConnect re-sends only changes), a reconnect resets them.
  - Not built (no task asked for them): the AIRPORT key's jump target, "unnamed waypoint", the slew guard at a list's first row, whole-list read-outs.
  - Task 7 waits on Task 6 Step 3: the pixel reader stays in the tree, unused by the window.
