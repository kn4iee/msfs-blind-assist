# BlueBird 757 Displays from Memory: the EHSI's VOR and ILS Modes, TCAS, the Map Options and the Rest of the Map (plan 28, added 2026-11-08, widened 2026-11-11)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched. Tasks marked [controller] or [owner] are never dispatched to an implementer.

**Goal:** The EHSI window (plan 26) also reads what the Captain's EHSI draws in VOR and ILS modes (course, deviation, TO/FROM, DME), TCAS traffic and its messages, the map options' items when selected (stations, waypoints, airports, the weather radar's labels), and what plan 26 measured on the map but left unread (TERR and its two elevations, the wind readout, the departure and arrival runway symbols, S/C, the green rings without a word and the altitude or time marker), so no mode says "not read yet" any more and every switch in the EHSI window changes its list.

**Architecture:** Plan 26's reader grows: four optional parts in `Bbs757EhsiLayout` (`Approach`, `Tcas`, `MapOptions`, `MapExtras`), each derived by plan 26's recipe extended here, read by its sampler and listed by its text. A part the recipe cannot derive costs only its rows ("<part>: not read: <reason>").

**Tech Stack:** C# 13 / .NET 10, WinForms, xUnit, the private toolkit, the b757-memory probe scripts (read-only).

**Spec:** `docs/superpowers/specs/2026-11-08-b757-memory-display-readers-design.md` (local), §5 "Plan 28"; the decisions S1-S17 are in plan 23's "The series" (S12). Plan 26 must have landed; this plan takes over its files. Evidence: `docs/b757/evidence/displays-ehsi.md` (plan 26's; Task 1 adds its sections); probe `findings.md` §4 (in the probed build: 23459 LOC/VOR, 23460 TCAS with TFC, TA ONLY, FAIL, OFF, TEST, TRAFFIC; 23365 VOR L/R, ILS, TO/FROM).

## Global Constraints

- Plan 26's Global Constraints apply (plan 23's through them), with the trailer `Plan: 28` and the artifacts path `<worktree>/bin/b757-p28`.
- **Rows in plan 26's window,** in screen order, each starting with where it sits; VOR and ILS: "Top right: ILS, <ident or frequency>, CRS 268, DME 12.3", "Center: course pointer 268, ahead", "Center: deviation 0.6 dots left" ("centred", "at full scale, left" where BlueBird pins it), "Right side: glideslope 1.2 dots below" (ILS), "Center: TO" or "FROM" (VOR); flags in place of the item, as drawn.
- **TCAS:** each traffic symbol drawn, "Map: traffic advisory, 6.2 NM, 30° right, +12 climbing" (BlueBird's kinds and its relative altitude in hundreds of feet, as drawn); the traffic itself is listed, not spoken (BlueBird's TCAS aurals speak); TCAS's own words (TFC, TA ONLY, TRAFFIC, OFF, FAIL, TEST) are messages, spoken under plan 26's "EHSI messages" row like the others.
- **Map options:** the stations, waypoints and airports the EHSI draws while their button is on, nearest first, by plan 26's `Bbs757EhsiGeometry.Place`; the weather radar's mode, tilt and gain labels as drawn. The radar's returns are a picture the reader cannot list: one row "Map: weather radar returns shown, not read"; **proposal (the owner confirms or drops it before Task 6):** a "Describe the picture" button in the EHSI window runs Alt+N's AI read of the EHSI.
- **What plan 26 left on the map (the owner, 2026-11-11: "update plan 28 to fix the gaps"),** each with what plan 26's evidence already holds (`docs/b757/evidence/displays-ehsi.md`; function numbers are the probed build's):
  - **TERR** (23427; `L:B757_ADI_1_TERR_BUTTON`, drawn in any mode while the IRS is valid): the word `TERR` top right and two three-digit numbers under it (terrain elevations in hundreds of feet, from a static pair per side), each colored by its height against the aircraft's altitude. Rows "Top right: TERR", "Top right: terrain 138, 004" with each number's color word as drawn. The terrain picture itself is a sweep the reader cannot list: one row "Map: terrain shown, not read", as the weather radar's.
  - **The wind** (23426; bottom left; MAP, VOR and ILS while the IRS is valid): `---`, or the speed (an integer, width 3) and an arrow turned by the wind direction less the map's rotation, shown only at 100 kt ground speed or more with the wind above 6 kt (a static show flag, cleared under 4 kt). Row "Bottom left: wind 70 kt from 30° left" (the arrow's direction off the top, by `Bbs757EhsiGeometry.Place`'s words) or "Bottom left: wind ---". Never spoken.
  - **The runways** (23446; records at `nd+968` departure and `nd+1032` arrival: two end points, the name, the length, the heading): the runway with its dashed centerlines and name. Rows "Map: runway 16L, <place>", by the nearer end.
  - **S/C** (39681): BlueBird computes it each frame from the legs' planned step altitudes or the step list; no field holds it. Task 1 decides with the owner: reproduce the computation (and prove it against a capture on a route with a step climb), or leave S/C unread and say so in the handoff.
  - **The green rings without a word** (the points of `P+4276` and `P+4288`; three were drawn along a descent) and **the altitude or time marker** (`P+200`, `P+204`, `P+208`; a ring with a rounded number or a four-digit time and `z`): Task 1 finds what the rings mark; a ring is listed only once it can be named from BlueBird's structure ("Map: <name>, <place>"), else one row "Map: 3 unnamed points" stands for them.
- **What plan 26 built that binds this plan:**
  - The window's rows are in screen order, top to bottom then left to right, each only inside `Bbs757EhsiGeometry.OnScreen`'s clip ([B757-37]); where this plan's older text says "nearest first", screen order stands (one order for the whole list).
  - The Alt+N window already holds the Captain's EFIS controls (Mode, Range, CTR, TFC, WXR, TERR, NAV AID, APRT, DATA, WPT); this plan adds none, and Task 6's button joins that panel. The switches are bytes in the nd (`nd+208` NAV AID, `+209` DATA, `+210` ARPT, `+211` WPT, `nd+304` TFC); the CTR flag is `u8 efis+348` (VOR and ILS drawn full rather than expanded).
  - BlueBird's words and the map's constants travel in the reading from the layout (`Bbs757EhsiWords`, `Bbs757EhsiConstants`); modes branch on the efis value (`ModeValue`), never on the word. New words and constants join those structs.
  - The top line's position is the simulator's (`PLANE LATITUDE`, `PLANE LONGITUDE`, [B757-39]); map places come from the nd's own center and rotation.
  - Not corrupt, only missing: a part's object not found, an index past its list. A torn list is corrupt ([B757-36]).
  - `EXCESS DATA` is `u32 nd+1276` reaching 100: a count of the background stations, airports and waypoints drawn this frame, zeroed each frame, so with ARPT or WPT on at a long range it can flicker between readings: Task 4 settles it (a message spoken only once it has stood for the monitor's settle; Review Focus).
  - In the recipe, origin and destination are told apart by code order and PLAN is named by elimination: revisit both when this plan's traces give a stronger fact.
  - A layout file with the new parts is installed only together with the build that reads it (a deployed build in use loses every 757 reader otherwise): the recipe's check run writes to a scratch folder, never to `%APPDATA%`, until Task 7's deploy.
- **Owned files:** plan 26's files (the EHSI reader, layout, recipe, text, wiring and their tests), `docs/b757/evidence/displays-ehsi.md` (its new sections), its rule lines. No new module reader: the shared lines are unchanged but for `GeneratorVersion.Current` at landing.

## Review Focus

- **ILS mode with a VOR frequency tuned** (or VOR mode with an ILS one): the rows show what BlueBird draws for that case, its message spoken once (Task 4 test `A_mode_and_frequency_mismatch_reads_as_drawn`).
- **A receiver that fails or loses the signal:** its flag in place of the deviation, never a frozen pointer (Task 4 test `A_lost_signal_shows_the_flag_not_the_pointer`).
- **Many traffic targets:** every drawn symbol listed, nearest first, the list re-sorted each refresh without speaking (Task 4 test `Traffic_is_listed_nearest_first_and_not_spoken`).
- **TCAS switched to TA ONLY or OFF mid-flight:** the message heard once (Task 4 test `A_tcas_mode_message_is_spoken_once`).
- **ARPT or WPT on at a long range, the count at the limit:** `EXCESS DATA` spoken once when it stands, never on and off each second (Task 4 test `Excess_data_that_flickers_is_spoken_once`).
- **The wind under 100 kt, or under 6 kt:** the row reads `---` as drawn, never a stale speed (Task 4 test `Wind_reads_dashes_when_the_screen_does`).
- **A map option on with nothing in range:** no rows for it, no error (Task 4 test `An_option_with_nothing_in_range_lists_nothing`).

---

### Task 0 [controller]: Claim

- [ ] **Step 1:** Plan 26's Status reads landed. `git fetch kn4iee`; `git switch -c B757-p28-ehsi-modes-tcas kn4iee/B757`; this file's Execution record; commit only this file, `docs(b757): claim plan 28`; `git push kn4iee HEAD:B757`. Private worktree at `feature/b757-displays`'s head.

### Task 1 [controller, simulator up, MSFSBA closed]: Measure

**Files:** `docs/b757/evidence/displays-ehsi.md` (new sections)

- [ ] **Step 1: VOR and ILS.** At a runway with its ILS tuned and a VOR tuned (owner's go-ahead for the radios and the EFIS mode): each item the draw prints in VOR and ILS (full and expanded, as BlueBird has them), its source, words and format, the deviation's scale and pin, the flags.
- [ ] **Step 2: TCAS.** BlueBird's `tcas_computer` (static slot 0xf12c8 in the probed build): its traffic list and each entry's fields (position or bearing and range, relative altitude, vertical trend, kind), the cap; live AI traffic or a recorded session; the TCAS messages and the mode they follow.
- [ ] **Step 3: The map options.** With STA, WPT, ARPT and WXR each on (owner's go-ahead): where the draw gets the items it shows (a list the EHSI keeps, or the nav database queried per frame), their fields and the clip; the weather radar's labels.
- [ ] **Step 4: The rest of the map.** TERR (the two numbers' statics, the color rule, the side index; `L:B757_ADI_1_TERR_BUTTON` on, owner's go-ahead); the wind (the fields the draw takes, the show flag; read only on the owner's flight above 100 kt, or from his logs); the runway records' fields with a departure and an arrival runway selected; the rings at `P+4276` and `P+4288` (who fills them: 24135) and the altitude or time marker; S/C: trace 39681 and put the choice (compute, or leave unread) to the owner with what each costs.
- [ ] **Step 5:** Write the sections (no snapshot); commit `docs(b757): plan 28's EHSI measurements`.

### Task 2: The layout's four parts

**Files:** `MSFSBlindAssist.DisplayLayouts/Bbs757/Bbs757EhsiLayout.cs`, `Bbs757LayoutDescriptions.cs` (`DescribeEhsi`); test `Bbs757EhsiLayoutTests.cs`

- [ ] **Steps 1-5:** `Bbs757EhsiLayout` gains the nullable parts `Bbs757EhsiApproach? Approach`, `Bbs757EhsiTcas? Tcas`, `Bbs757EhsiMapOptions? MapOptions`, `Bbs757EhsiMapExtras? MapExtras` (TERR, the wind, the runways, the rings and marker, S/C if Task 1 chose to compute it), each with its `…Missing` reason; tests `Each_part_round_trips_byte_identical` (de-DE, sv-SE), `A_missing_part_keeps_its_reason`, `A_file_without_the_parts_reads_as_missing`; commit `feat(b757): the EHSI layout's VOR, ILS, TCAS, map option and map extra parts`.

### Task 3 [private repo]: The recipe's parts

- [ ] **Steps:** merge the app branch in; tests `Derives_the_approach_part`, `Derives_the_tcas_part`, `Derives_the_map_options_part`, `Derives_the_map_extras_part`, `A_part_it_cannot_derive_is_missing_not_a_failure`, and a broken-step case per new step in `Each_broken_step_fails_naming_it`; implement in `Bbs757EhsiRecipe.cs`; toolkit suite; generate into a scratch folder (plan 26's fields unchanged, every new field equals Task 1's; nothing to `%APPDATA%` before Task 7); commit `feat(bbs757): the EHSI recipe's VOR, ILS, TCAS, map option and map extra parts` (private, not pushed).

### Task 4: Read, list and speak them

**Files:** `MSFSBlindAssist/Services/BBS757/Bbs757EhsiReader.cs`, `Bbs757EhsiReading.cs`, `Bbs757EhsiText.cs` (a partial `Bbs757EhsiText.Approach.cs` and `Bbs757EhsiText.Tcas.cs` if it grows past 400 lines), `Bbs757EhsiDisplay.cs`; tests `Bbs757EhsiApproachTests.cs`, `Bbs757EhsiTcasTests.cs`, `Bbs757EhsiMapOptionsTests.cs`, `Bbs757EhsiMapExtrasTests.cs`

- [ ] **Step 1: Write the failing tests** (the readings Task 1 recorded, through the layout): `Ils_mode_reads_course_deviation_and_dme`, `Vor_mode_reads_to_from`, `A_mode_and_frequency_mismatch_reads_as_drawn` (Review Focus), `A_lost_signal_shows_the_flag_not_the_pointer` (Review Focus), `Traffic_is_listed_nearest_first_and_not_spoken` (Review Focus), `A_tcas_mode_message_is_spoken_once` (Review Focus), `An_option_with_nothing_in_range_lists_nothing` (Review Focus), `Excess_data_that_flickers_is_spoken_once` (Review Focus), `Wind_reads_dashes_when_the_screen_does` (Review Focus), `Terr_reads_its_word_and_two_elevations`, `A_runway_is_listed_by_its_nearer_end`, `Unnamed_points_are_counted_not_invented`, `Each_window_switch_changes_the_list` (WXR, TERR, TFC, NAV AID, APRT, WPT each on: at least its row), `A_missing_part_lists_not_read_with_its_reason`, `Rows_format_under_de_DE_and_sv_SE`, and the TCAS walk `A_torn_traffic_list_is_corrupt`.
- [ ] **Steps 2-5:** FAIL; implement (the walks all-or-nothing, in plan 26's `RememberRegions` scope; "not read yet" removed); PASS and the full suite; commit `feat(b757): the EHSI's VOR, ILS, TCAS, map options and the rest of the map`.

### Task 5: Rules

- [ ] **Step 1:** Extend plan 26's all-or-nothing line to the TCAS and map option walks (or the next free number, one mechanism per ID); `ClaudeContextBudgetTests` green; commit `docs(b757): the EHSI's new walks`.

### Task 6 [owner]: The picture button

- [ ] **Step 1:** Ask the owner about the proposal in Global Constraints. If yes: a "Describe the picture" button at the end of the EHSI window's existing controls panel (plan 26's EFIS controls in `BBS757Definition.Ehsi.cs`; a mnemonic none of them uses) running `context.AiRead()`, test `The_picture_button_runs_the_ai_read`, commit `feat(b757): describe the EHSI's picture from its window`. If no: record it in the execution record.

### Task 7 [controller, simulator up]: Live checks, deploy, publish

- [ ] **Steps:** rebase (app and private), the next `GeneratorVersion.Current`, regenerate with every reader on `B757`, suites green; read-only: ILS and VOR rows against an EHSI capture at a runway; traffic against the simulator's AI traffic positions; deploy to `C:\MSFSBA-B757` with the layout; ask the owner to push and publish.

## In-sim checks (owner, by ear; rewritten 2026-11-12 after the measurements and the whole-plan review)

Only what needs a flight or the owner's ears; Task 7's live checks on the ground are the controller's (the rows
against captures in each mode, the switches, the tuned station, the course line, the FIX circle, the TCAS words).

1. An ILS approach (Alt+N open, EFIS on APP, expanded then CTR): "deviation … right/left" agrees with which way to
   fly; "glideslope … above" while below the path; the DME is the ILS's; the frequency row reads dashes until the
   localizer is received. Nothing is spoken when turning the mode selector or pressing CTR.
2. VOR mode on a tuned station: TO then FROM passing it; about 5 degrees a dot.
3. TCAS in flight: "TA ONLY" heard once on the knob, and its "gone" once climbing through 1,000 ft radio altitude
   with TA/RA set; with traffic near, how often "EHSI message: TRAFFIC" is heard on the ground at a busy airport
   (the owner decides whether TRAFFIC stays spoken); a target below reads a tag like "-05".
4. Cruise: "Bottom left: wind 70 kt from 30° left" against the screen's arrow; dashes under 100 kt.
5. TERR on near terrain: the two numbers and their colors, with the gear up and with it down close to the amber
   boundary (BlueBird compares the gear with a value that may mean up).
6. Level flight with the MCP altitude set lower: no altitude range arc row; in a climb or descent toward it: a
   plausible distance.
7. A VNAV descent: which sign of "VNAV path deviation" is above the path (the row gives the signed number until this
   is heard); "Map: deceleration starts / ends" near the 250 kt slow-down; OFFPATH DES on: its circles row, and the
   reader keeps running.
8. A long flight: no "not read: unexpected values" row and no "EHSI unavailable"; if one shows, the
   `bbs757-ehsi.log` at once (it rotates within minutes).

## Handoff to plan 16

- **Pilot-facing:** "The 757's EHSI window also reads VOR and ILS modes with the bearing pointers, TCAS traffic and its messages, the map options, the wind, the terrain elevations, the runways, the deceleration points and the FIX page's rings, with the altitude range arc and the VNAV path deviation." Changelog `improvement`; the guide's 757 EHSI part (the weather radar is not shown on this display; S/C is on the PROGRESS page).
- **docs/b757.md:** the EHSI reader's parts.

## Execution record

- **Status:** done (2026-11-12): landed at the owner's word ("Push and publish"), with the generator-8 757 layout published; the owner's flight (KDEN to CYVR, 2026-11-12) passed what it exercised, results below; closed at his word the same day, the checks it did not exercise deferred (below)
- **Claimed by:** Robin Kipp (robin24), 2026-11-12
- **Worked by:** Robin Kipp with Claude (subagent-driven development)
- **Started:** 2026-11-12, from `B757` 22d54e0d0 (plan 26 landed)
- **Landed:** 2026-11-12, a fast-forward of `B757` from the claim 353488970 (nothing else had landed meanwhile). Shared lines: no new reader (the `ehsi` reader gained five optional parts); `GeneratorVersion.Current` 8; rules B757-40 to B757-49 in `b757-ehsi`. Deployed to `C:\MSFSBA-B757` (d9398c68f) with its layout. Private `feature/b757-displays` pushed at 77dd8df95; the generator-8 757 layout `…-5c145a6233d04a83.json` published (layouts 3f9604ef1) beside the generator-7 file, which still loads (the new parts are optional), so it stays; `LayoutBundle` passes on the branch with this build (2 files for bbs757, 3 for pmdg737).
- **Tests at landing:** the app suite 25,032 passed, 0 failed, at d9398c68f; the solution builds with 0 errors; the toolkit suite 701 passed (private, at 82cf62bcb; unchanged by the version raise).
- **Tasks:** 0 claim (353488970); 1 measure (56198a036, e64ff937c); 2 the layout's parts (04013dd56; fields added
  or removed by 789c49788, ef1091d22, 041f5f4e9); 3 the recipe, in the private `feature/b757-displays` (not pushed):
  TCAS 77868fe05 and 56fd0b1e9, VOR and ILS 973917282, 74e579f14, 713baedf2 and 885cf50ba, map options 8815cef7e and
  666f933f6, map extras 36f4bf048 and 241ccca1c, map guides 6cfbc446d, c33dce9c0 and 82cf62bcb, generator version 8
  8a651803f, with the app branch merged in between; 4 the reader: VOR, ILS and TCAS 21b3c8e55 and 789c49788, map
  options ef1091d22, TERR, wind, runways and deceleration points 81d61ca4e, the guides 7ad16a0a8, review polish
  f34b4e4ea; 5 rules B757-40 to B757-49 (f2c14fc05, 03e3c7a6e); 6 no picture button (the owner); the whole-plan
  review's fixes 041f5f4e9, 0000b345c and d9398c68f; 7 the live checks and the deploy (below). Each dispatch reviewed
  by a fresh reviewer, each fix round re-reviewed, then one whole-plan review of the app branch.
- **Waiting on the owner:** nothing. The checks the flight did not exercise (CTR, a TO to FROM passage, terrain
  colors and the gear boundary, OFFPATH DES and the FIX page in flight) are deferred, not required: the owner closed
  the plan with "the remaining checks are for mostly edge case scenarios which can wait until some future point".
- **In-sim results:** the owner's flight KDEN to CYVR, 2026-11-12, recorded beside the app by a throwaway read-only
  recorder (the app's own reader and rows once a second with the simulator's values, 3 h 46 min, about 13,500
  readings; never committed):
  - Health: one corrupt reading in the recorder and one in the app's log, at different moments, each gone on the
    next reading; no "not read: unexpected values", no "EHSI unavailable". Nothing spoken at any mode change.
  - Check 1 (ILS, CYVR 111.95, expanded): intercepting from 184 m left of the centerline the row read "0.3 dots
    right"; "glideslope … above" while below the path, centered at 6.3 NM and 2,008 ft, "0.1 dots below" about 50 ft
    high; the DME ran down to 0.1 at the touchdown zone; dashes in ILS mode until the frequency was set. CTR: not
    exercised.
  - Check 2 (VOR, MLD, course 000, passed abeam): FROM throughout; full scale left, 2.0 dots at about 10 degrees
    off (5 degrees a dot), centered on the radial, full scale right; out of range: `DME ---`, no bar, no pointer.
    A TO to FROM passage: not exercised.
  - Check 3 (TCAS): "TA ONLY" once on the knob and gone once in the climb (the owner heard it once on the approach
    too, where the raw reading flickered for 3 s around 1,000 ft); tags below from "-00" to "-26"; the self-test
    spoke nothing. `TRAFFIC` came about 12 times in 19 minutes of taxi at KDEN and 7 times at CYVR; **the owner
    keeps it spoken** ("super helpful"). Three were one-second repeats right after a real one in the raw reading;
    whether they are heard twice is not known.
  - Check 4 (wind): within 1 kt and 2 degrees of the simulator all through cruise; dashes by BlueBird's flag (until
    liftoff, and with the wind near 3 kt on the approach).
  - Check 5 (TERR): "000 green, 000 green" over water at range 10; other colors and the gear boundary: not
    exercised.
  - Check 6 (range arc): no row in level cruise; distances right in the climb and the descent (7.2 NM at 2,350
    ft/min with 2,560 ft to go at 404 kt).
  - Check 7 (VNAV): **positive is the aircraft above the path** (level at 5,000 ft the value ran from -1,108
    through 0 to +175 as the path came down); the row now says "above the path" or "below the path" ([B757-48]).
    The deceleration rows, T/C and T/D appeared and counted down. OFFPATH DES and the FIX page: not exercised.
  - Check 8: as under Health.
- **Ground checks:** the controller's, 2026-11-12, parked at KSEA,
  MSFSBA closed: the app's own reader and rows run read-only against the simulator with the generator-8 layout, each
  compared with a capture of the EHSI:
  - MAP at range 20 with NAV AID, APRT, WPT, TERR and TFC on: every row as the screen (the waypoints in screen order,
    KBFI, KSEA, the tuned SEA, runway 16L, the FIX circle, traffic with its tags, `TERR` 031 red and 004 amber,
    `EXCESS DATA`, wind dashes).
  - VOR expanded and centered (SEA 116.80, course 187): DME 0.5, the course pointer, "deviation 0.5 dots right", TO,
    VOR L; centered: `TA ONLY` still shown, `TRAFFIC` after the left block, no terrain picture row.
  - ILS centered and expanded (110.30 ISNQ, course 164): DME 0.6, ILS, 110.30, the deviation and the glideslope at
    full scale, the bearing pointer to SEA; the panel parked: `DME ---`, no deviation bar, dashes.
  - PLAN at range 80: `TERR` 105 red, the FIX fix with its 15 NM circle and radial 300, T/C, T/D, the two
    deceleration points, the heading bug. One miss, fixed (d9398c68f): a manually tuned VOR's course line is drawn in
    PLAN with its station off the screen.
  - One reading takes 4.0 ms light and 4.3 ms full, 5.8 ms with every map option on (medians of 20).
  - Deployed build d9398c68f with the layout `…-5c145a6233d04a83.json` in `%APPDATA%` beside the generator-7 file.
- **Changed from the plan:** from Task 1's measurements (the evidence file has each):
  - **No weather radar:** BlueBird's EHSI draws none; the WXR switch changes nothing on it (traced and captured). No
    radar rows or labels, and WXR is not among the switches that change the list.
  - **No course number, station name or VOR frequency is printed** in VOR or ILS: the course is its own row
    ("Center: course pointer 187, <place>"), from the value the pointer is drawn from.
  - **Traffic rows are in screen order** (Global Constraints), so Task 4's test is
    `Traffic_is_listed_in_screen_order_and_not_spoken`.
  - **The map options need BlueBird's loop replayed:** one item from each switched-on list per round, counted to
    100; the reader replays it to list exactly the items drawn. The tuned VOR stations and the VOR course lines are
    drawn with every switch off and are listed.
  - **The green rings** are the start and the end of the next deceleration in the descent (named from the descent
    profile's speed-change segments; no string names them): "Map: deceleration starts", "Map: deceleration ends".
  - **The bearing pointers** (the RMI's two green pointers) belong to the VOR and ILS picture and are read.
  - **The amber `VOR` flag is not read:** its condition is an electrical circuit lookup, not a field. LOC and G/S are.
  - **A plan 26 defect, fixed here:** a navaid leg's position follows the leg's capability set; a TACAN-only leg read
    0, 0 by the primary position.
  - **L:vars the draw reads** come from BlueBird's variable handle in memory (the cached value); the simulator
    inputs added are `AMBIENT WIND VELOCITY`, `AMBIENT WIND DIRECTION` and `GEAR POSITION` (3).
  - **The owner's decisions (2026-11-12):** S/C and the FIX page's PRED ETA-ALT marker stay unread ("the Prog page
    shows when there's a step climb coming up"). The FIX page's rings, circles and radials, the off-path descent
    circles, the altitude range arc and the VNAV path deviation scale are added to this plan (a fifth optional layout
    part, `MapGuides`, through Tasks 2 to 4). No "Describe the picture" button (Task 6: no).
  - **From the reviews and the live checks:**
    - A part that keeps reading unexpected values is dropped alone ("<part>: not read: unexpected values") after 4
      torn readings in a row, and comes back on its next good read; the route, Ctrl+W and the other parts keep
      running ([B757-40], [B757-41]). While a part is out, its words are neither spoken as gone nor as new.
    - `TCAS FAIL` hides the other words only with the gate byte set; the TCAS self-test speaks nothing; `TFC` and
      `TEST` are never spoken (the pilot's own presses); `TCAS FAIL` is.
    - VOR and ILS also list the ETA (expanded) and the heading bug; the bearing pointers give an angle only; in ILS
      mode the pointer uses the panel's ILS-mode station.
    - The layout file format lets a member marked `[LayoutFileOptional]` be absent, so files written before these
      parts still load ([WASM-26]'s full text); a deployed older build ignores the new members of a newer file.
    - Rules B757-40 to B757-49 in `b757-ehsi.md` (the walks, the optional parts, the loop replay, listed against
      spoken, inputs from memory, the navaid leg position, the draw's own conditions, magnetic against true, the
      measured signs, what a reading may read).
    - The recipe's map option part needs its VOR and ILS part (it cross-checks the same records).
- **Open at landing:**
  - Not read: S/C and the FIX page's PRED ETA-ALT marker (the owner); the amber `VOR` flag and the VOR panel's power
    (an electrical lookup, not a field: an unpowered, manually tuned VOR still lists its pointer and course line);
    whether a tuned station is still received; the position trend vector; a FIX fix entered as a position with no
    stored name (BlueBird builds its name).
  - Approximations: a
    300 NM circle at range 10 is sampled every 54 px or so; a course line's listing in MAP under the TRU reference
    is turned by the heading number, off by the variation; a part's torn-reading count is not reset by readings in
    which the part is not read; staggered tears of several parts can still add up to more than the monitor's ten.
  - Not measured live: the LOC and G/S flags, a resolution advisory, the off-path circles, centered VOR and ILS in
    flight, a TO to FROM passage, terrain colors near the gear boundary, deceleration points for an inactive route.
  - For plan 16 (the handoff below): the changelog and guide lines; the hotkey guide is unchanged.
  - Private repository: the toolkit document's section for these parts.
