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

## In-sim checks (owner, by ear)

1. ILS mode on an approach: course, deviation and DME follow the needles; a flag heard once if one shows.
2. TCAS with traffic around: Alt+N lists it; TA ONLY heard once when set.
3. NAV AID and APRT on in the window: the stations and airports appear in the list, in screen order; off, they go.
4. In cruise: the wind row with its speed; TERR on: its word and two numbers; a departure or arrival runway listed near the airport.
5. Each of the window's switches (WXR, TERR, TFC, NAV AID, APRT, WPT) changes the list when set.

## Handoff to plan 16

- **Pilot-facing:** "The 757's EHSI window also reads VOR and ILS modes, TCAS traffic, the map options, the wind, the terrain elevations and the runways." Changelog `improvement`; the guide's 757 EHSI part.
- **docs/b757.md:** the EHSI reader's parts.

## Execution record

- **Status:** in progress
- **Claimed by:** Robin Kipp (robin24), 2026-11-12
- **Worked by:** Robin Kipp with Claude (subagent-driven development)
- **Started:** 2026-11-12, from `B757` 22d54e0d0 (plan 26 landed)
- **Landed:**
- **Tests at landing:**
- **Tasks:** 0 claim (353488970), 1 measure (56198a036), 2 the layout's parts (04013dd56).
- **Waiting on the owner:** nothing.
- **In-sim results:**
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
- **Open at landing:**
