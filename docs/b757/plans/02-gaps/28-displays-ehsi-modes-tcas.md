# BlueBird 757 Displays from Memory: the EHSI's VOR and ILS Modes, TCAS and the Map Options (plan 28, added 2026-11-08)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched. Tasks marked [controller] or [owner] are never dispatched to an implementer.

**Goal:** The EHSI window (plan 26) also reads what the Captain's EHSI draws in VOR and ILS modes (course, deviation, TO/FROM, DME), TCAS traffic and its messages, and the map options' items when selected (stations, waypoints, airports, the weather radar's labels), so no mode says "not read yet" any more.

**Architecture:** Plan 26's reader grows: three optional parts in `Bbs757EhsiLayout` (`Approach`, `Tcas`, `MapOptions`), each derived by plan 26's recipe extended here, read by its sampler and listed by its text. A part the recipe cannot derive costs only its rows ("<part>: not read: <reason>").

**Tech Stack:** C# 13 / .NET 10, WinForms, xUnit, the private toolkit, the b757-memory probe scripts (read-only).

**Spec:** `docs/superpowers/specs/2026-11-08-b757-memory-display-readers-design.md` (local), §5 "Plan 28"; the decisions S1-S17 are in plan 23's "The series" (S12). Plan 26 must have landed; this plan takes over its files. Evidence: `docs/b757/evidence/displays-ehsi.md` (plan 26's; Task 1 adds its sections); probe `findings.md` §4 (in the probed build: 23459 LOC/VOR, 23460 TCAS with TFC, TA ONLY, FAIL, OFF, TEST, TRAFFIC; 23365 VOR L/R, ILS, TO/FROM).

## Global Constraints

- Plan 26's Global Constraints apply (plan 23's through them), with the trailer `Plan: 28` and the artifacts path `<worktree>/bin/b757-p28`.
- **Rows in plan 26's window,** in screen order, each starting with where it sits; VOR and ILS: "Top right: ILS, <ident or frequency>, CRS 268, DME 12.3", "Center: course pointer 268, ahead", "Center: deviation 0.6 dots left" ("centred", "at full scale, left" where BlueBird pins it), "Right side: glideslope 1.2 dots below" (ILS), "Center: TO" or "FROM" (VOR); flags in place of the item, as drawn.
- **TCAS:** each traffic symbol drawn, "Map: traffic advisory, 6.2 NM, 30° right, +12 climbing" (BlueBird's kinds and its relative altitude in hundreds of feet, as drawn); the traffic itself is listed, not spoken (BlueBird's TCAS aurals speak); TCAS's own words (TFC, TA ONLY, TRAFFIC, OFF, FAIL, TEST) are messages, spoken under plan 26's "EHSI messages" row like the others.
- **Map options:** the stations, waypoints and airports the EHSI draws while their button is on, nearest first, by plan 26's `Bbs757EhsiGeometry.Place`; the weather radar's mode, tilt and gain labels as drawn. The radar's returns are a picture the reader cannot list: one row "Map: weather radar returns shown, not read"; **proposal (the owner confirms or drops it before Task 6):** a "Describe the picture" button in the EHSI window runs Alt+N's AI read of the EHSI.
- **Owned files:** plan 26's files (the EHSI reader, layout, recipe, text, wiring and their tests), `docs/b757/evidence/displays-ehsi.md` (its new sections), its rule lines. No new module reader: the shared lines are unchanged but for `GeneratorVersion.Current` at landing.

## Review Focus

- **ILS mode with a VOR frequency tuned** (or VOR mode with an ILS one): the rows show what BlueBird draws for that case, its message spoken once (Task 4 test `A_mode_and_frequency_mismatch_reads_as_drawn`).
- **A receiver that fails or loses the signal:** its flag in place of the deviation, never a frozen pointer (Task 4 test `A_lost_signal_shows_the_flag_not_the_pointer`).
- **Many traffic targets:** every drawn symbol listed, nearest first, the list re-sorted each refresh without speaking (Task 4 test `Traffic_is_listed_nearest_first_and_not_spoken`).
- **TCAS switched to TA ONLY or OFF mid-flight:** the message heard once (Task 4 test `A_tcas_mode_message_is_spoken_once`).
- **A map option on with nothing in range:** no rows for it, no error (Task 4 test `An_option_with_nothing_in_range_lists_nothing`).

---

### Task 0 [controller]: Claim

- [ ] **Step 1:** Plan 26's Status reads landed. `git fetch kn4iee`; `git switch -c B757-p28-ehsi-modes-tcas kn4iee/B757`; this file's Execution record; commit only this file, `docs(b757): claim plan 28`; `git push kn4iee HEAD:B757`. Private worktree at `feature/b757-displays`'s head.

### Task 1 [controller, simulator up, MSFSBA closed]: Measure

**Files:** `docs/b757/evidence/displays-ehsi.md` (new sections)

- [ ] **Step 1: VOR and ILS.** At a runway with its ILS tuned and a VOR tuned (owner's go-ahead for the radios and the EFIS mode): each item the draw prints in VOR and ILS (full and expanded, as BlueBird has them), its source, words and format, the deviation's scale and pin, the flags.
- [ ] **Step 2: TCAS.** BlueBird's `tcas_computer` (static slot 0xf12c8 in the probed build): its traffic list and each entry's fields (position or bearing and range, relative altitude, vertical trend, kind), the cap; live AI traffic or a recorded session; the TCAS messages and the mode they follow.
- [ ] **Step 3: The map options.** With STA, WPT, ARPT and WXR each on (owner's go-ahead): where the draw gets the items it shows (a list the EHSI keeps, or the nav database queried per frame), their fields and the clip; the weather radar's labels.
- [ ] **Step 4:** Write the sections (no snapshot); commit `docs(b757): plan 28's EHSI measurements`.

### Task 2: The layout's three parts

**Files:** `MSFSBlindAssist.DisplayLayouts/Bbs757/Bbs757EhsiLayout.cs`, `Bbs757LayoutDescriptions.cs` (`DescribeEhsi`); test `Bbs757EhsiLayoutTests.cs`

- [ ] **Steps 1-5:** `Bbs757EhsiLayout` gains the nullable parts `Bbs757EhsiApproach? Approach`, `Bbs757EhsiTcas? Tcas`, `Bbs757EhsiMapOptions? MapOptions`, each with its `…Missing` reason; tests `Each_part_round_trips_byte_identical` (de-DE, sv-SE), `A_missing_part_keeps_its_reason`, `A_file_without_the_parts_reads_as_missing`; commit `feat(b757): the EHSI layout's VOR, ILS, TCAS and map option parts`.

### Task 3 [private repo]: The recipe's parts

- [ ] **Steps:** merge the app branch in; tests `Derives_the_approach_part`, `Derives_the_tcas_part`, `Derives_the_map_options_part`, `A_part_it_cannot_derive_is_missing_not_a_failure`; implement in `Bbs757EhsiRecipe.cs`; toolkit suite; generate (plan 26's fields unchanged, every new field equals Task 1's), copy to `%APPDATA%`; commit `feat(bbs757): the EHSI recipe's VOR, ILS, TCAS and map option parts` (private, not pushed).

### Task 4: Read, list and speak them

**Files:** `MSFSBlindAssist/Services/BBS757/Bbs757EhsiReader.cs`, `Bbs757EhsiReading.cs`, `Bbs757EhsiText.cs` (a partial `Bbs757EhsiText.Approach.cs` and `Bbs757EhsiText.Tcas.cs` if it grows past 400 lines), `Bbs757EhsiDisplay.cs`; tests `Bbs757EhsiApproachTests.cs`, `Bbs757EhsiTcasTests.cs`, `Bbs757EhsiMapOptionsTests.cs`

- [ ] **Step 1: Write the failing tests** (the readings Task 1 recorded, through the layout): `Ils_mode_reads_course_deviation_and_dme`, `Vor_mode_reads_to_from`, `A_mode_and_frequency_mismatch_reads_as_drawn` (Review Focus), `A_lost_signal_shows_the_flag_not_the_pointer` (Review Focus), `Traffic_is_listed_nearest_first_and_not_spoken` (Review Focus), `A_tcas_mode_message_is_spoken_once` (Review Focus), `An_option_with_nothing_in_range_lists_nothing` (Review Focus), `A_missing_part_lists_not_read_with_its_reason`, `Rows_format_under_de_DE_and_sv_SE`, and the TCAS walk `A_torn_traffic_list_is_corrupt`.
- [ ] **Steps 2-5:** FAIL; implement (the walks all-or-nothing, in plan 26's `RememberRegions` scope; "not read yet" removed); PASS and the full suite; commit `feat(b757): the EHSI's VOR, ILS, TCAS and map options`.

### Task 5: Rules

- [ ] **Step 1:** Extend plan 26's all-or-nothing line to the TCAS and map option walks (or the next free number, one mechanism per ID); `ClaudeContextBudgetTests` green; commit `docs(b757): the EHSI's new walks`.

### Task 6 [owner]: The picture button

- [ ] **Step 1:** Ask the owner about the proposal in Global Constraints. If yes: a "Describe the picture" button in the EHSI window's controls area (`BBS757DisplayForm`'s `controls`) running `context.AiRead()`, test `The_picture_button_runs_the_ai_read`, commit `feat(b757): describe the EHSI's picture from its window`. If no: record it in the execution record.

### Task 7 [controller, simulator up]: Live checks, deploy, publish

- [ ] **Steps:** rebase (app and private), the next `GeneratorVersion.Current`, regenerate with every reader on `B757`, suites green; read-only: ILS and VOR rows against an EHSI capture at a runway; traffic against the simulator's AI traffic positions; deploy to `C:\MSFSBA-B757` with the layout; ask the owner to push and publish.

## In-sim checks (owner, by ear)

1. ILS mode on an approach: course, deviation and DME follow the needles; a flag heard once if one shows.
2. TCAS with traffic around: Alt+N lists it; TA ONLY heard once when set.
3. STA and ARPT on: the stations and airports listed nearest first.

## Handoff to plan 16

- **Pilot-facing:** "The 757's EHSI window also reads VOR and ILS modes, TCAS traffic and the map options." Changelog `improvement`; the guide's 757 EHSI part.
- **docs/b757.md:** the EHSI reader's parts.

## Execution record

- **Status:** not started
- **Claimed by:**
- **Worked by:**
- **Started:**
- **Landed:**
- **Tests at landing:**
- **Tasks:**
- **Waiting on the owner:**
- **In-sim results:**
- **Changed from the plan:**
- **Open at landing:**
