# BlueBird 757 Displays from Memory: the EHSI's Map and Plan (plan 26, added 2026-11-08)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched. Tasks marked [controller] or [owner] are never dispatched to an implementer.

**Goal:** Alt+N opens an "EHSI" window that lists what the Captain's EHSI draws in MAP and PLAN (mode, range, the heading and track line, the top line with the active waypoint, the route's waypoints with their distance and bearing, T/C, T/D, S/C, E/D, the heading bug, the messages), read from `BBS752.wasm` memory; the EHSI's messages and the active waypoint's changes are spoken; output Ctrl+W says the top line. The AI read stays Alt+N's fallback.

**Architecture:** A reader behind plan 23's monitor (`IBbs757DisplayReader<Bbs757EhsiLayout, Bbs757EhsiReading>`), registered through the `RegisterEhsi` seam. It reads a light set every tick (mode, range, heading and track, the top line's sources, the messages) and the full set (the route) while the window is shown or Ctrl+W asks, as the 737's PFD reader does. BlueBird computes distances and the ETA while it draws, so pure `Bbs757EhsiGeometry` computes them the same way, from the legs and the aircraft's position, by the formula Task 1 traces in the draw. The route walk is all-or-nothing.

**Tech Stack:** C# 13 / .NET 10, WinForms, xUnit, the private toolkit, the b757-memory probe scripts (read-only).

**Spec:** `docs/superpowers/specs/2026-11-08-b757-memory-display-readers-design.md` (local), §5 "Plan 26"; the decisions S1-S17 are in plan 23's "The series" (S12 the split with plan 28, S13 Ctrl+W). Plan 23 must have landed; it runs beside plans 24 and 25. Evidence: `docs/b757/evidence/displays-ehsi.md` (Task 1 writes it); probe `findings.md` §3 "EHSI route" (`nd+908` → `fms`, `nd+1244` → route, legs at `+668`, 1,632 bytes a leg, ident `std::string` +0, latitude and longitude doubles +80 and +88 in the probed build) and §4 (the EHSI's draw functions: 23366 main, 23424 mode and range, 23425 top line, 23448 T/C T/D S/C E/D, 23406 EXCESS DATA).

## Global Constraints

- Plan 23's Global Constraints apply, with the trailer `Plan: 26` and the artifacts path `<worktree>/bin/b757-p26`.
- **Only what is drawn:** a map item is listed only where the EHSI draws it, inside the map at this mode and range by BlueBird's own clip (Task 1); each row starts with where it sits ("Top right:", "Map:"), as the 737's ND window does; BlueBird's printed text and formats (the ETA's "z", "NM", its decimals).
- **Places on the map:** "<item>, <distance> NM, <n>° left/right" or "ahead" off the top of the map (the track in a track-up mode, north in PLAN; Task 1 records which); in PLAN, from the plan's center waypoint, "5.0 NM north-west of KSEA".
- **Not read yet (S12):** in VOR or ILS mode the header rows read and one row "VOR mode: not read yet" (or "ILS mode: not read yet") stands for the rest until plan 28; TCAS traffic, the map options' items and the VOR/ILS rows are plan 28's.
- **Ctrl+W (S13):** `HotkeyAction.ReadNDWaypoint` says the top line as drawn ("KPDX, 1533.3z, 23.0 NM"), interrupting, whether or not the window is open; "No active waypoint"; "EHSI waypoint unavailable: <reason>" while the reader cannot run. It is the only key this plan adds; the hotkey guide's line is plan 16's.
- **Shared lines (S16):** `ehsi` at the end of the module's readers and its recipe at the end of `Bbs757Recipes.All`; the next `GeneratorVersion.Current` at landing.
- **No gate (the owner, 2026-11-11, [B757-30]):** S3's comparison with the simulator is gone from every 757 reader; this reader never compares memory with a simulator value. Trust is structural only: the layout for this build, the objects found by vptr, every read inside the module, and `Corrupt`. A simulator variable is only an input where BlueBird's draw takes one (S10, the reader's `SimInputs`). Comparing with the simulator while measuring (Task 1) is still a good way to identify a field.
- **Owned files:** new `MSFSBlindAssist/Services/BBS757/Bbs757Ehsi*.cs`, `MSFSBlindAssist.DisplayLayouts/Bbs757/Bbs757EhsiLayout.cs`, its dump in `Bbs757LayoutDescriptions.cs`, its reader line in `Bbs757Module.cs`, `MSFSBlindAssist/Aircraft/BBS757Definition.Ehsi.cs`, `tests/MSFSBlindAssist.Tests/Bbs757Ehsi*Tests.cs`, `docs/b757/evidence/displays-ehsi.md`, its rule lines; private `Bbs757EhsiRecipe.cs`, its fixture and tests, its line in `Bbs757Recipes.cs`. Plan 28 takes them over once this plan has landed.

## Review Focus

- **A route crossing the date line or a pole-near leg:** distances and bearings from the great-circle formula BlueBird uses, no wrap error (Task 5 test `Bearings_wrap_across_the_date_line`).
- **The active waypoint sequencing every few seconds on an approach:** each change spoken once after the settle, never a backlog (Task 6 test `Quick_sequencing_says_only_the_waypoint_it_rests_on`).
- **A discontinuity in the route:** no leg invented across it; the rows show what BlueBird draws there (Task 4 test `A_discontinuity_is_read_as_drawn`).
- **The pilot changes range or mode while the window is open:** the rows follow on the next refresh; the change itself is not spoken (the screen reader speaks the panel's combo, CORE-7) (Task 6 test `A_mode_or_range_change_is_shown_not_spoken`).
- **No route at all** (cold and dark, or the FMC empty): the window says what the EHSI shows, "No active waypoint" on Ctrl+W, no stale route (Task 6 test `No_route_reads_no_active_waypoint`).

---

### Task 0 [controller]: Claim

- [ ] **Step 1:** Plan 23's Status reads landed. `git fetch kn4iee`; `git switch -c B757-p26-ehsi-map kn4iee/B757`; this file's Execution record: claimed, Claimed by, Started; commit only this file, `docs(b757): claim plan 26`; `git push kn4iee HEAD:B757`. Private worktree at `feature/b757-displays`'s head.

### Task 1 [controller, simulator up, MSFSBA closed]: Measure

**Files:** `docs/b757/evidence/displays-ehsi.md`

- [ ] **Step 1: The objects.** In the current build: the Captain's `nd` object (static slot 0xf1364 in the probed build) and how a recipe reaches it without names (the gauge manager, as the FMA's pfd, by the nd class vptr), `nd`'s pointers to `fms`, the route, the EFIS control panel and the instrument source selector.
- [ ] **Step 2: Mode, range, heading and track.** Their fields and words (the mode draw's HDG, TRK, MAP, PLAN, VOR, ILS, EXP and the like; the range values), against the EFIS panel's L:vars (the control map's EFIS mode and range selectors' `state_var`), each set once (owner's go-ahead; put back).
- [ ] **Step 3: The top line and the map items.** With a route entered (KSEA-KPDX and a direct-to, owner's go-ahead): the top line's draw (function 23425 in the probed build): which fields or computations give the waypoint, ETA and distance, and the formula (great circle or rhumb, the ETA from ground speed); the legs' fields (ident, position, the discontinuity marker, a leg's kind); T/C, T/D, S/C, E/D (function 23448: their positions or distances along the route); the heading bug; the map's clip at each range in MAP and PLAN; the messages (function 23406 and the others: every word drawn); a capture of the EHSI view (`PrintWindow`) beside each reading.
- [ ] **Step 4: The position.** Where the draw takes the aircraft's position (the FMS position in memory, or a simulator variable, which then is an S10 input), identified against `PLANE LATITUDE` and `PLANE LONGITUDE` on the ground and in a turn; nothing is compared at run time ([B757-30]).
- [ ] **Step 5:** Write the evidence (no snapshot); commit `docs(b757): plan 26's EHSI measurements`.

### Task 2: The layout record

**Files:** create `MSFSBlindAssist.DisplayLayouts/Bbs757/Bbs757EhsiLayout.cs`; modify `Bbs757Module.cs` (`public const string Ehsi = "ehsi";`, its reader last), `Bbs757LayoutDescriptions.cs` (`DescribeEhsi`); test `Bbs757EhsiLayoutTests.cs`

**Interfaces:**
- Produces: `Bbs757EhsiLayout` with the anchor and manager, the nd vptr, the route's chain and leg fields (`LegStride`, `LegIdent`, `LegLatitude`, `LegLongitude`, the discontinuity test, `MaxLegs`), mode and range fields with `Bbs757WordTable Modes` and the range table, the heading, track and heading bug fields, the top line's sources, the VNAV markers' fields, the messages' fields and words, the position fields the draw takes; content equality.

- [ ] **Step 1: Write the failing tests:** `The_ehsi_layout_round_trips_byte_identical` (de-DE, sv-SE), `A_file_without_the_ehsi_reader_reads_as_missing`. **Step 2:** FAIL. **Step 3:** Implement. **Step 4:** PASS and the full suite. **Step 5:** Commit `feat(b757): the EHSI layout record`.

### Task 3 [private repo]: The recipe

**Files:** `Bbs757EhsiRecipe.cs`, one line in `Bbs757Recipes.cs`, `Bbs757EhsiFixture.cs`, `Bbs757EhsiRecipeTests.cs`

- [ ] **Step 1:** Merge the app branch in. **Step 2: Write the failing tests:** `Derives_the_nd_route_and_leg_fields`, `Derives_the_mode_words_and_range_table`, `Derives_the_top_line_sources`, `Derives_the_vnav_markers_and_messages`, `A_second_mode_draw_fails_with_its_step`. **Step 3:** FAIL. **Step 4:** Implement. **Step 5:** PASS and the toolkit suite. **Step 6:** Generate from the installed file; every field equals Task 1's; the other readers unchanged; copy to `%APPDATA%`. Commit `feat(bbs757): the EHSI recipe` (private, not pushed).

### Task 4: The sampler

**Files:** `MSFSBlindAssist/Services/BBS757/Bbs757EhsiReader.cs`, `Bbs757EhsiReading.cs`; test `Bbs757EhsiReaderTests.cs`

**Interfaces:**
- Produces: `public sealed record Bbs757EhsiReading(string? Mode, double? Range, double? Heading, double? Track, double? HeadingBug, Bbs757EhsiWaypoint? Active, IReadOnlyList<Bbs757EhsiLeg>? Route, IReadOnlyList<Bbs757EhsiMarker>? Markers, IReadOnlyList<string> Messages, double Latitude, double Longitude, bool Corrupt) : IBbs757Reading` (`Route` and `Markers` null in a light reading; `Active`, the active leg's ident and position with the top line's other sources, is in every reading, so Ctrl+W never needs the full set); `public readonly record struct Bbs757EhsiWaypoint(string Ident, double Latitude, double Longitude)`; `public readonly record struct Bbs757EhsiLeg(string Ident, double Latitude, double Longitude, bool Discontinuity)`; `public readonly record struct Bbs757EhsiMarker(string Word, double Latitude, double Longitude)`; `Bbs757EhsiReader : IBbs757DisplaySampler<Bbs757EhsiReading>` with `public Func<bool> FullWanted { init; }`.

- [ ] **Step 1: Write the failing tests** on `FakeProcessMemory` with Task 1's live route placed through the layout: `Reads_the_light_set_every_time`, `Reads_the_route_only_when_full_is_wanted`, `A_route_pointer_outside_the_module_is_corrupt`, `A_leg_seen_twice_is_corrupt`, `More_legs_than_the_cap_is_corrupt`, `A_discontinuity_is_read_as_drawn` (Review Focus).
- [ ] **Step 2:** FAIL. **Step 3:** Implement (one `RememberRegions` scope; reads in blocks). **Step 4:** PASS. **Step 5:** Commit `feat(b757): read the EHSI from memory`.

### Task 5: The geometry

**Files:** `MSFSBlindAssist/Services/BBS757/Bbs757EhsiGeometry.cs`; test `Bbs757EhsiGeometryTests.cs`

**Interfaces:**
- Produces (pure): `public static (double Nm, double BearingTrue) DistanceAndBearing(double lat1, double lon1, double lat2, double lon2)` (Task 1's formula), `public static double OffTop(double bearingTrue, double upTrue)` (−180 to 180, minus left), `public static bool OnMap(string mode, double range, double nm, double offTop)` (BlueBird's clip from Task 1), `public static string Place(double nm, double offTop)` ("23.0 NM, 4° right", "ahead" within half a degree), `public static string PlanPlace(double nm, double bearingTrue, string center)` ("5.0 NM north-west of KSEA").

- [ ] **Step 1: Write the failing tests:** `Distance_and_bearing_match_the_measured_route` (Task 1's KSEA-KPDX values), `Bearings_wrap_across_the_date_line` (Review Focus), `Off_top_is_signed_left_minus`, `The_clip_matches_each_measured_range`, `Places_format_invariantly_under_de_DE_and_sv_SE`.
- [ ] **Step 2:** FAIL. **Step 3:** Implement. **Step 4:** PASS. **Step 5:** Commit `feat(b757): the EHSI's geometry`.

### Task 6: The rows and the reader

**Files:** `Bbs757EhsiText.cs`, `Bbs757EhsiDisplay.cs`; tests `Bbs757EhsiTextTests.cs`, `Bbs757EhsiDisplayTests.cs`

**Interfaces:**
- Produces: `Bbs757EhsiDisplay` with `public Func<bool> FullWanted { get; set; }` (handed to every sampler it makes; false until Task 7 sets it), `Name = "EHSI"`, `LogChannel = "bbs757-ehsi"`, `TickMs = 500`, `MuteKey = "MSFSBA_BBS757_EHSI_MESSAGES"` (row "EHSI messages", which also carries the active waypoint line); `public string WaypointLine()` for Ctrl+W; `Bbs757EhsiText.Rows(reading)`: "EHSI: MAP, range 40", "Top: TRK 079", "Top right: KPDX, 1533.3z, 23.0 NM", "Map: <waypoint>, <place>" per drawn leg, "Map: T/D, <place>", "Map: heading bug, <n>° right", "Message: <word>" each or "Messages: none"; in VOR or ILS the header and "VOR mode: not read yet".
- Changes: "EHSI message: <words>", "EHSI message gone: <words>" (items of one settle joined), "EHSI: <waypoint>, <whole NM> NM" when the active waypoint changes, "EHSI: no active waypoint".

- [ ] **Step 1: Write the failing tests:** text `Rows_follow_the_screen_top_to_bottom`, `Only_items_on_the_map_are_listed`, `Vor_mode_says_not_read_yet`, `Numbers_and_times_format_as_drawn_under_de_DE_and_sv_SE`; display `Quick_sequencing_says_only_the_waypoint_it_rests_on` (Review Focus), `A_mode_or_range_change_is_shown_not_spoken` (Review Focus), `No_route_reads_no_active_waypoint` (Review Focus), `A_new_message_is_spoken_once`, `The_waypoint_line_while_unavailable_names_the_reason`.
- [ ] **Step 2:** FAIL. **Step 3:** Implement. **Step 4:** PASS. **Step 5:** Commit `feat(b757): the EHSI's rows and words`.

### Task 7: The window, Alt+N, Ctrl+W and Ctrl+M

**Files:** `MSFSBlindAssist/Aircraft/BBS757Definition.Ehsi.cs`; test `Bbs757EhsiWiringTests.cs`

**Interfaces:**
- Produces: `partial void RegisterEhsi(Bbs757DisplayRegistry r)`: the monitor, the Ctrl+M row, `HotkeyAction.ReadDisplayND` (fallback `context.AiRead()`, else `BBS757DisplayForm("EHSI", "Captain EHSI", monitor.View)`; the display's `FullWanted` is the window's `Visible`), and `HotkeyAction.ReadNDWaypoint` (`context.Announcer.AnnounceImmediate(display.WaypointLine())`, from the latest light reading).

- [ ] **Step 1:** Declare the simulator inputs the draw takes, if Task 1 found any (S10, the display's `SimInputs`; the definition registers each hidden where not already, [VAR-7]). **Step 2: Write the failing tests:** `Alt_N_opens_the_ehsi_window_while_the_reader_runs`, `Alt_N_falls_back_to_the_ai_read_when_unavailable`, `Ctrl_W_says_the_top_line_interrupting`, `Ctrl_W_answers_with_the_window_closed`, `The_ctrl_m_row_is_listed`. **Step 3:** FAIL. **Step 4:** Implement. **Step 5:** PASS and the full suite. **Step 6:** Commit `feat(b757): the EHSI window on Alt+N, its waypoint on Ctrl+W`.

### Task 8: Rules

- [ ] **Step 1:** The next free B757 numbers: the all-or-nothing route walk (`Bbs757EhsiReader`); only what is drawn (`Bbs757EhsiText` lists an item only inside BlueBird's clip, `Bbs757EhsiGeometry.OnMap`); Ctrl+W on the 757 (`ReadNDWaypoint` is the EHSI's top line, interrupting). `ClaudeContextBudgetTests` green. Commit `docs(b757): the EHSI rules`.

### Task 9 [controller, simulator up]: Live checks, deploy, publish

- [ ] **Step 1:** Rebase (app and private); the next `GeneratorVersion.Current` if another display plan landed; regenerate with every reader on `B757`; layout tests and the full suite green.
- [ ] **Step 2:** Read-only, on the ground with a route: the window's rows against an EHSI capture at ranges 10, 40 and 160 in MAP and in PLAN; Ctrl+W against the top line; `bbs757-ehsi.log` reaches Running.
- [ ] **Step 3:** Deploy to `C:\MSFSBA-B757` with the regenerated layout; ask the owner to push and publish.

## In-sim checks (owner, by ear)

1. A flight's departure: Alt+N lists the route ahead with distances that shrink; each waypoint passed heard once ("EHSI: <next>, <n> NM").
2. Ctrl+W at any time, window open or closed: the top line.
3. T/D in cruise: listed with its distance; a message (EXCESS DATA at a long range with many items, if it shows) heard once.
4. Ctrl+M "EHSI messages" off: no EHSI line; Ctrl+W still answers (a key-press answer).

## Handoff to plan 16

- **Pilot-facing:** "Alt+N opens the 757's EHSI: the route, the active waypoint and the VNAV points, with distances and bearings; Ctrl+W says the active waypoint." Changelog `feature`; the guide's 757 displays part and its keys.
- **docs/b757.md:** the EHSI reader; Alt+N's fallback. **HotkeyGuides/BBS757_Hotkeys.txt:** Alt+N's line and the new Ctrl+W line.

## Execution record

- **Status:** claimed
- **Claimed by:** Robin Kipp (robin24), 2026-11-11
- **Worked by:** Robin Kipp with Claude (subagent-driven development)
- **Started:** 2026-11-11, from `B757` ba06e4d2c (plans 23, 24 and 25 landed, no gate in any 757 reader)
- **Landed:**
- **Tests at landing:**
- **Tasks:**
- **Waiting on the owner:**
- **In-sim results:**
- **Changed from the plan:**
- **Open at landing:**
