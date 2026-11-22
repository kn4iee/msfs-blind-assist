# Plan 03 evidence: the MCDU on the display reader

Two sessions, both MSFS 2024 (`FlightSimulator2024.exe` 1.9.24.0) with the A330-200 (GE):

- **2026-11-20, on the ground**, cold and dark to a programmed MCDU (KATL to KMCO, typed by Claude, flight number
  MSF773): the captain's MCDU frames the plan's "Measured on 2026-11-20" section describes. The fixtures
  `p03_*.json` (eighteen) are some of them, the frame alone (drawn text, positions, fonts, colours), with
  `"hiddenRowsLeftOut": true` added because those frames held the screen's rows only. No page that holds the
  owner's own route or anything the owner typed was made a fixture (spec section 4).
- **2026-11-22, in flight**, the display reader 1.0.0 (protocol 1): reads of both MCDUs and the lamps, and on the
  first officer's MCDU only a few keys through the calculator path, put back as found (the owner had said that day:
  "The sim is also up and in flight if needed").

## Task 1: the grid

The geometry of `A330McduGeometry` (a text's first character at Left 22 + column x 16.87, its baseline at Top 43 +
row x 25.85, nearest cell, half-way to the lower one) was checked against every captured frame of the captain's MCDU
of 2026-11-20 (142 frames, the owner's pages included; checked, not committed): every text starts inside the 14 rows
and 24 columns, and no two texts claim one cell with different characters. Spaces were not counted as claiming a
cell (ruling F3: a space writes nothing).

Texts off a column or a row, as the Review Focus lists: "AVAILABLE RUNWAYS" on DEPARTURES starts at column 3.44
(it takes column 3); the "1/7" of P/N XLOAD sits at row 0.54 (it takes row 1, the nearer one, under the title).

## Task 2 Step 1: the special characters (M3)

What each code draws, where it was seen, and the source. "Frames" are the 2026-11-20 captures; "labels" are the
pixel reader's training labels (`tools/a330-cdu-ocr/data/big/truth.json`, removed in Task 7), which name the same
glyphs as the characters the window now shows for them.

| Code | Drawn | Seen on (frames) | Labels | Window shows |
| --- | --- | --- | --- | --- |
| `o` | an entry box | INIT (`oooooooooo     oooo/oooo`), PERF TAKE OFF (`ooo`), FIX INFO, NEW WAYPOINT | not labelled | `-`, the Fenix MCDU window's box stand-in |
| `d` | the degree sign | INIT (`---- /---d`), IRS INIT, RAD NAV, HOLD, PERF TAKE OFF (`[  ]d`), CLIMB WIND (`[ ]d`) | `°` (27 times) | `°` |
| `a`, `b` | left and right arrows | beside a title (`INIT        ab`), before a list row (`a08L`), after a transition | `←`, `→` | `←`, `→` |
| `c` | the overfly mark | after a waypoint, alone on the scratchpad line | not labelled | `Δ`, the Fenix MCDU window's overfly mark |
| `e`, `f` | up and down arrows | bottom right of the F-PLN (`ef`) | `↑`, `↓` | `↑`, `↓` |
| `"` | ditto: same as the row above | F-PLN speed and altitude columns (`" / 11160`) | not labelled | `"` |
| `*`, `<`, `>`, `[`, `]`, `(`, `)`, `+`, `-`, `.`, `/`, `:`, `=`, `%` | themselves | everywhere | themselves | themselves |

Each shows as one cell (the braille rule). Wording of each stand-in: the owner's (Task 2 Step 3).

## In flight, 2026-11-22

- The captain's MCDU on an ARRIVAL list page: a complete frame of 50 texts in 0.1 s, with no colour (M14) and
  without `hiddenRowsLeftOut` (M15). The rows of the list the MCDU does not show were in the frame on rows 14 to 29,
  below the screen, and one of them on the scratchpad line (row 13). So with today's reader a list page is refused
  by `A330McduScreen.From` (a text starts off the grid), and the scratchpad line cannot be trusted while
  `HiddenRowsLeftOut` is false: the Global Constraints' rule holds for both the window and the FMS message
  call-out. (The page held the owner's route: not captured as a fixture.)
- `INI_FMS1_message_light` 0 and `INI_FMS2_message_light` 0, with no FMS message on either scratchpad line.
  `INI_FMC_PAGE1` 4.

### The first officer's MCDU, typed line and a refused entry (Task 4 Step 3, Task 5 Step 2)

Keys through the simulator's calculator path (each with its own prefix), on the first officer's MCDU only, the app's
own protocol to the display reader (the typed line and the frames, as `A330ReaderClient` receives them):

| Step | Row 13 of the frame | Typed line |
| --- | --- | --- |
| On INIT (as found) | no frame in the first 6 s | `""` |
| DATA (DATA INDEX 1) | (empty); complete frames from then on | `""` |
| X | `X` | `"X"` |
| SPACE | `X` | `"X "`: a trailing space is kept, so a SPACE is confirmed like any other key |
| LSK4R (an empty line) | (empty) | `""`: the entry was dropped with no message |
| INIT, X, LSK5L (COST INDEX) | `FORMAT ERROR` | `""`: the typed line never holds a displayed message |
| Y, with FORMAT ERROR shown | `Y` | `"Y"`: typing replaces a white message |
| CLR | (empty) | `""` (the first CLR sent did not arrive; the second did) |

- `INI_FMS2_message_light` stayed 0 while FORMAT ERROR showed: a white message does not light it. An amber FMS
  message was not tried. So the lamp sentence of Task 5 Step 2 speaks at most for amber messages, not measured yet.
- Both pages the reader gave for the first officer's MCDU (DATA INDEX 1, INIT) place every text inside the grid with
  no two texts on one cell: the same geometry as the captain's.
- Put back as found: the first officer's MCDU on INIT, its cost index unchanged, the scratchpad empty. The
  frames of the INIT page hold the owner's route: none was kept as a fixture.

## Task 6 Step 1 (offline): the window against the cockpit's pages

Eighteen captured pages of 2026-11-20 (Claude's test route), each pinned with the window's whole listing in
`A330McduPagesTests`: MCDU MENU, INIT A and B, DATA INDEX 1 and 2, A/C STATUS, P/N XLOAD, four F-PLN screens (three
slews), LAT REV at a waypoint, DEPARTURES (runways and SIDs), PERF TAKE OFF, PROG, RAD NAV, FUEL PRED. The expected
listings came from a second, separate implementation of the plan's rules (the controller's, not committed); all
eighteen agreed with the window's at the first run. The owner's walk of the same pages in the window, on both sides
and with the owner's route, is Step 3.

## Task 6 Step 2: the window against the app's other Airbus MCDU windows, and braille

Compared on the eighteen pages `A330McduPagesTests` pins, against `FenixMCDUForm` and `FlyByWireMCDUForm` (their line
builders and `FenixMcduFormat` / `FbwMcduFormat`). Nothing below was changed: each is a candidate for the owner.

**The same as the other two windows:** a label row indented three cells, a data row `n: ` and its text, `n:` for a line
with nothing on it (as the FlyByWire window; the Fenix leaves an empty data row out when it has a label),
`Scratchpad: ` before the scratchpad line (new in this plan: the pixel-read window showed the line alone), every row
right-trimmed with its columns kept, Ctrl+Shift+L/R and the Left/Right selector (the Fenix's), the page keys
(Task 4b), the degree sign, the arrows and the overfly mark as the Fenix shows them.

**Braille:** a field's cell is its column plus three on both label and data rows (the label row's three-cell indent
and the data row's `n: ` are the same width), so a right-hand field is in the same cell on every row; no row passes 40
cells with its prefix (`No_row_with_its_prefix_passes_40_cells`); one screen row a list item; the status row is a
read-only box, first in the tab order; the scratchpad line is always listed.

**Candidates (different from the Fenix or the FlyByWire window, or open):**

1. **Title.** The A330 lists the title row as drawn, trimmed (`INIT        ←→`), with the page arrows where the
   cockpit draws them; the Fenix writes `Title: <title>`, the FlyByWire `Title: <title>   <page>` and its arrows as
   `▲ ▼ ◄ ►` at the end. A `Title: ` prefix would match both.
2. **Entry boxes.** The Fenix's stand-in `-` (used here, ruling C1) makes a required box row read like a dash row that
   needs nothing: INIT shows `1: ----------     ----/----` (boxes: FROM/TO required) next to
   `2: ----/----------` (dashes). Spec D4 wants an amber box row to read "as empty and required". A one-cell stand-in
   of its own (the owner's Task 2 Step 3 ruling) would tell them apart.
3. **Greyed prompts.** Only the A330 marks them: `(unavailable)` after a prompt, or `(inop)` where only the short
   form fits in the gap before the next field (`<TO DATA (inop)   PHASE>`), or `(unavailable)` without its blank at
   the end of a full row (`<FM1           NAV B/UP>(unavailable)`). The wording and the short form are Claude's.
4. **The page number of P/N XLOAD** (`1/7`, drawn half a row below the title) lands on row 1, a label row, under the
   title: `                        1/7`. Read as a label, not as the title's page number.
5. **The F-PLN slew arrows** are drawn on the scratchpad line, so the listing ends `Scratchpad: NAV ACCURACY UPGRAD
   ↑↓`; the FlyByWire window puts its arrows on the title line. The scratchpad read-back and the FMS message call-out
   leave them out (Tasks 4 and 5).
6. **Not built (no task asked for them; the plan's "Measured on 2026-11-20" describes them):** the AIRPORT key saying
   which airport it jumped to; a flight plan row with no name read as "unnamed waypoint"; never sending the slew key
   that moves to earlier rows while a list shows its first row; a whole-list or whole-flight-plan read-out.
7. **The window shows no page rows until the display reader leaves the hidden list rows out (spec M15).** Measured
   2026-11-22: today's reader sends no `hiddenRowsLeftOut`, so every page lists only "List rows not read yet" and the
   FMS message call-out says nothing; the message lamps of Task 5 Step 2 say a message is waiting (the white ones do
   not light them). The plan says the window ships once M15 is solved or the owner rules otherwise.
8. **A line key's read-back looks at the pressed line only**: a key whose effect lands on another row is said as "No
   change" (pinned on purpose). Saying "No change on line N", or comparing every row, would mislead less.
9. **A side switch** says "Left MCDU" or "Right MCDU" only; the Fenix then says the new side's title too.

## Announcements against the A32NX and the Fenix

- **An FMS message with the window closed:** the A32NX says the message alone (`FbwMcduReadBack`); the Fenix window
  says the scratchpad line alone; the A330 says the message alone (it said "MCDU message: <message>" until the
  whole-plan review; the plan's example).
- **Backspace on a message:** the A32NX and the Fenix speak the line's next state; the A330 says "Message cleared:
  <message>" (the plan's Review Focus) and then the line's next state: for the owner to rule.
- **The lamps:** the Fenix's "MCDU FM Captain: On" / "MCDU FM FO: On".
