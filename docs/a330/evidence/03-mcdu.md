# Plan 03 evidence: the MCDU on the display reader

Two sessions, both MSFS 2024 (`FlightSimulator2024.exe` 1.9.24.0) with the A330-200 (GE):

- **2026-11-20, on the ground**, cold and dark to a programmed MCDU (KATL to KMCO, typed by Claude, flight number
  MSF773): the captain's MCDU frames the plan's "Measured on 2026-11-20" section describes. The fixtures
  `p03_*.json` are five of them, the frame alone (drawn text, positions, fonts, colours), with
  `"hiddenRowsLeftOut": true` added because those frames held the screen's rows only. No page that holds the
  owner's own route or anything the owner typed was made a fixture (spec section 4).
- **2026-11-22, in flight**, the display reader 1.0.0 (protocol 1), reads only: nothing was written to the
  simulator.

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

## In flight, 2026-11-22 (reads only)

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
| On INIT, still (as found) | no frame yet: the window would show "Reading the first officer's MCDU" until the page changed | `""` |
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
- Put back as found: the first officer's MCDU on INIT, its cost index unchanged (120), the scratchpad empty. The
  frames of the INIT page hold the owner's route: none was kept as a fixture.
