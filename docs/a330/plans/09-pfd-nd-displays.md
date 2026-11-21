# A330 PFD, ND, ISIS and DCDU Implementation Plan (plan 09, later)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** The flight modes are spoken in the PFD's own words, read from its mode band as drawn; no table of mode numbers to words is built (spec D7). Output Alt+P, Alt+N, Alt+I and Ctrl+Shift+D open live PFD, ND, ISIS and DCDU windows, each with the cockpit controls that change it (spec D9). ND messages, the next waypoint and DCDU uplinks are called out. Since 2026-11-21 the reader reads the PFD and the ND whole (spec D14, M9, M11), so neither window waits on a go or no-go.

**Architecture:** The PFD's mode band is the source of the flight modes' words: a pure band model (columns and lines by measured position) turns a complete PFD frame into each column's words. The mode L:vars, which change at once, only ask the reader for a fresh PFD frame, so a call-out speaks the band's new words as soon as the PFD shows them; while the reader gives no PFD frame, the call-outs keep today's words from the numbers. The ND, ISIS and DCDU are read as plan 08 reads the ECAM displays: a pure page model by measured position, a pure text step, plan 02's window with key rows, and announcers that compare complete frames.

**Tech Stack:** C# 13 / .NET 10 Windows Forms, xUnit; plan 02's `IA330Displays`, `A330DisplayWindow` and replay source; the reader program's `capture` and `bench` commands for the sim tasks.

**Spec:** `docs/a330/spec.md` sections 2.1, 2.7, 3 (R4, R16, R17), 5 (D3, D7, D8, D9, D14, D15), 6 (M9, M10, M11, M14); `docs/ini-a330.md` "Flight modes"; the Fenix's rules for the same displays as the wording yardstick (`.claude/rules/fenix-channel-displays.md`).

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply.
- Owned files: `Aircraft/IniA330Definition.FlightDisplays.cs`, `Aircraft/A330/A330Fma.cs`, `Aircraft/A330/Pfd/**`, `Nd/**`, `Isis/**`, `Dcdu/**` (new), `Aircraft/A330/A330CalloutRows.Pfd.cs`, `.Nd.cs`, `.Datalink.cs`, `Forms/IniA330/A330PfdWindow.cs`, `A330NdWindow.cs`, `A330IsisWindow.cs`, `A330DcduWindow.cs` (new), tests `tests/MSFSBlindAssist.Tests/A330/A330Fma*Tests.cs`, `A330Pfd*Tests.cs`, `A330Nd*Tests.cs`, `A330Isis*Tests.cs`, `A330Dcdu*Tests.cs`, fixtures `p09_*.json`; `docs/ini-a330.md` "Flight modes"; `docs/a330/evidence/09-displays.md` (new).
- A flight mode's word is one the PFD drew, read from its band. While the reader gives no PFD frame (it is starting, or not installed), the words are today's from the numbers, and a number with no word is spoken as today ("vertical mode 14"). No word comes from another aircraft, and today's table is not extended from flights (spec D7).
- Colours wait for the reader (spec M14): until a frame carries them, no colour word is said, on the band or the ND.
- Colour words follow spec D8's table, which follows the Fenix's windows. PFD: only the flight mode band's words say their colour ("Vertical: ALT, cyan"), in the window and in the call-outs; the tapes, values and flags say none. ND: amber and red always, any other colour only where it is unusual for that kind of item or tells two items apart ([FNX-41]'s rule; the usual colours of each kind of item are measured from the fixtures, as `FenixChannelNdSpeech` holds them). ISIS and DCDU: none.
- A map item's place is said as a distance and a direction from the aircraft, never as screen coordinates.
- Window keys press the panel's controls through `PressFromWindow` by their keys in plan 04's map (the EFIS panels exist today; the standby instrument's and the DCDUs' keys come with plan 04's Task 5: a window lists the keys the map has when it is built, and its test builds in-memory rows).

## Measured on 2026-11-21: both read whole

With the reader's current approach (spec section 2.1, D14), on the owner's preflight with the route
uplinked, the IRS aligned and the take-off data entered:

- **PFD: the whole frame**, every item: the mode band (SRS, ALT, NAV, 1, FD, 2, A/THR), the altitude
  tape's numbers, QNH and 1018, the speeds. The first approach had always missed one to three items.
- **ND with the route: the whole frame**: KATL and 09L by the runway, the waypoints (1530, LIDAS, ERWIN,
  GSU), the top lines, the cross-track figure (0.2L), the distance (0.9NM). With the first approach the
  same ND read none of its items once the route showed.
- **Colours** are not in the frames yet (spec M14). The band's colour words and the ND's amber and red
  wait for them; nothing else here does.
- So Task 1 measures the built reader instead of deciding, and the two go or no-go decisions below are
  closed: the PFD window lists the PFD's text, and Task 4's window promises the route's items.

## Measured on 2026-11-20

- **Task 1 (M9), with the first approach:** the PFD could not be read whole (superseded on 2026-11-21,
  above).
- **The flight modes are text** (Task 2 Step 1, started): at the gate with the captain's flight director
  on, the armed row read ALT and NAV in cyan, with `1`, the FD symbol and `-` at the right; with it off
  the band is empty. The speed tape drew V1 in cyan and V2 in magenta.
- The PFD shows only a window of each tape: the frame holds the numbers on the screen.
- **Self test:** SELF TEST IN PROGRESS / MAX 40 SECONDS, as text, on the PFD and the ND. Before the IRS
  align the PFD draws no text at all and the ND reads HDG and MAP NOT AVAIL in red.
- **Task 4, the ND:** with the IRS aligned and no route, complete in 1 s (KATL, the nearest navaid, the
  cross-track figure, GPS PRIMARY). **With a route at 20 and 80 NM the first approach did NOT read it
  complete** (at 10 NM it did: see below) in any mode (ROSE LS, VOR, NAV, ARC, PLAN at 20 and 80 NM): at
  best 7 of 13 items in 10 s. What did come was right (the runway, INTCPT, SNUFY, SLAWW, RIGGZ, RIVTT, the
  cross-track figure). Superseded on 2026-11-21 (above): the ND with a route reads whole.
- A mode or range change draws a message on the ND for a moment.

Later the same day, on the owner's own preflight (the plan uplinked from the INIT page, flight directors
on, IRS aligned during the session):

- **IRS off:** the PFD draws no text at all; the ND draws HDG and MAP NOT AVAIL in red and, top right,
  the next leg all the same (its name, course and distance: "1530", "106", "0.8 NM").
- **PFD, IRS aligned, flight directors on:** 38 items; the first approach read at most 37 of them. In
  the mode band: "V/S" in green with its value in cyan beside it, and "1",
  the FD symbol and "2" at the right (both flight directors). On the speed tape V1 in cyan and V2 in
  magenta (142 and 148 that day). An amber X twice (drawn where a value is not valid). QNH and its
  value, the altitude and heading tapes' numbers.
- **ND, ARC at 10 NM, the uplinked route, IRS aligned: complete**, 16 items: ground speed and "---" true
  airspeed top left, the wind as "---/---", the next leg top right, KATL and 09L by the runway, the
  cross-track figure "0.2L", a navaid in cyan, the next waypoint, GPS PRIMARY at the bottom.
- **Text outside the display.** The ND drew a waypoint behind the aircraft (LIDAS) at a position below
  the bottom edge of the display, with no clip set. The frame carries it; Task 4's map list drops any
  item whose position lies outside the display's own area (its size is a named constant of `A330NdLayout`, from
  the fixtures), or it would name waypoints a sighted pilot cannot see.
- The degree sign on the ND is the font's `!`; the FD symbol on the PFD is the letters `fd` in its font:
  each display's special characters get a table of their own, as the MCDU's have (plan 03).
- **ISIS:** complete, including its flags and the count-down while it aligns ("INIT 53S").
- **DCDUs:** nothing drawn while empty, and the two cannot be told apart yet (Task 7).

## Review Focus

- A call-out speaking the band's old words because the PFD frame it used was drawn before the L:vars changed: after a number change the call-out waits for a frame read after the change, at most 1 s, then speaks today's words from the numbers; it is never silent and never waits longer (Task 2 tests).
- Two modes drawn in one column (an active and an armed line): the band keeps them by their line, never joined (Task 2).
- An ND waypoint's direction said from the screen's top in a mode whose top is not the aircraft's heading (PLAN mode is north up): the direction rule is per mode (Task 4 test for each mode).
- A DCDU message spoken twice, once on arrival and again when the pilot opens the window: spoken once per arrival (Task 7 test).
- The first officer's ND and PFD showing something else than the captain's: each window reads its own gauge (tests on two fixtures).

---

### Task 0 [controller]: Claim this plan

As plan 01's Task 0, with this file's path and `docs(a330): claim plan 09` (`Plan: 09`). Start from the branch once plan 02 has landed.

### Task 1 [sim]: The PFD and the ND on the built reader (M9, M11)

**Files:** the evidence file; fixtures `p09_pfd_*.json`, `p09_nd_*.json`.

- [ ] **Step 1:** With the IRS aligned and the route loaded, on the ground and (from plan 11, or a short flight with the owner) in the air: the reader's `bench PfdCaptain 60` and `bench NdCaptain 60`. Record how often a frame is complete and the time from a change on the display to its frame at the app (spec D15: under 3 s).
- [ ] **Step 2:** Capture `p09_pfd_*.json` (flight directors off and on, and each ground-reachable mode) and `p09_nd_<mode>_<range>.json` (Task 4 Step 1), and list in the evidence file which of the PFD's items are text (speeds, altitudes, the mode band, messages) and which never appear (drawn as symbols: the self test showed its flags are not text).
- [ ] **Step 3:** If the built reader's figure falls short of D15 on either display, the record says by how much, and the claimant tells the reader plan's claimant; this plan carries on with the fixtures.

### Task 2: The flight modes in the PFD's own words (R17, M10)

**Files:** Create `Aircraft/A330/Pfd/A330FmaBand.cs`; modify `Aircraft/A330/A330Fma.cs`, `Aircraft/IniA330Definition.FlightDisplays.cs`; tests `tests/MSFSBlindAssist.Tests/A330/A330FmaBandTests.cs`, `A330FmaTests.cs`.

**Interfaces:**
- Produces:
  ```csharp
  namespace MSFSBlindAssist.Aircraft.A330.Pfd;
  /// The PFD's mode band from a complete frame: each text with its column 1..5 and line 1..3, and its colour once frames carry one (spec M14).
  internal static class A330FmaBand { public static IReadOnlyList<(int Column, int Line, string Text, string? Colour)> From(A330Frame frame); }
  /// The five columns' words, as the flight mode rows and call-outs say them: active line, then armed lines.
  internal sealed record A330FmaRead(string Thrust, string Vertical, string Lateral, string Armed, string Capability, string Autopilot, long ReadTicks)
  { public static A330FmaRead? From(IReadOnlyList<(int Column, int Line, string Text, string? Colour)> band, long readTicks); }
  ```
  The band's geometry (which strip of the PFD, the five columns' x ranges, the three lines' baselines) is measured in Step 1 and held in `A330FmaBand` as named constants with the fixture that proves them.

  `CurrentFma()` gives the newest `A330FmaRead` when one was read after the last change of the mode numbers, else today's words from the numbers. A call-out after a number change asks the reader for a fresh PFD frame (`Want` with a short refresh for 2 s), and speaks when a frame read after the change arrives, or after 1 s with today's words; it is never silent. A colour is said only when the band carries one (spec D8: "Vertical: ALT, cyan"); until then none.

- [ ] **Step 1 [sim]:** On the ground, with the owner's go-ahead: flight directors on, and capture the band as each ground-reachable mode shows (CLB armed, SRS, a heading or track selection, an ALT selection, the autothrust states); record the band's geometry in the evidence file.
- [ ] **Steps 1-5:** tests: `The_band_gives_each_columns_words_by_line`, `An_armed_line_is_kept_apart_from_the_active_line`, `The_band_ignores_texts_outside_its_strip`, `An_incomplete_frame_gives_no_band`, `A_call_out_after_a_number_change_uses_a_frame_read_after_it`, `A_call_out_never_waits_more_than_one_second`, `With_no_PFD_frame_the_call_out_uses_todays_words`, `A_colour_is_said_only_when_the_band_carries_one`, de-DE; implement: while the A330 is active the definition wants `PfdCaptain` at the background refresh; commit `feat(a330): flight mode words from the PFD`.

### Task 3: The PFD window

**Files:** Create `Aircraft/A330/Pfd/A330PfdText.cs`, `Forms/IniA330/A330PfdWindow.cs`; modify `Aircraft/IniA330Definition.FlightDisplays.cs`; tests `A330PfdTextTests.cs`, `A330PfdWindowTests.cs`.

- [ ] **Steps 1-5:** rows: the flight modes by column ("Thrust: ...", "Vertical: ..., armed ...", "Lateral: ...", "Approach capability: ...", "Autopilot: ...") from `CurrentFma()`, each mode word followed by the colour the band drew it in once frames carry colours ("Vertical: V/S, green; ALT, cyan"), worded as the Fenix's flight mode rows are, and with no colour until then; no other PFD row says a colour (tests `A_mode_word_says_the_bands_colour`, `No_tape_value_or_flag_says_a_colour`, `Modes_say_no_colour_when_the_band_carries_none`); then the PFD's drawn texts in screen order (plan 08's rule: as drawn, named only by a drawn label, else with their place); while the reader gives no PFD frame, the speed, altitude, heading, vertical speed and altimeter setting from the variables the read-out keys use, and one row "PFD not read yet". Tests: `The_modes_come_first_by_column`, `Drawn_text_is_listed_when_a_frame_comes`, `Alt_P_opens_the_window_and_falls_back_to_the_AI_read_when_the_reader_is_not_running`, `The_window_carries_the_captains_FD_LS_and_altimeter_controls`, de-DE; `A330PfdWindow : A330DisplayWindow` (title "A330 PFD", list "PFD display"); commit `feat(a330): the PFD window`.

### Task 4 [sim]: The ND

**Files:** Create `Aircraft/A330/Nd/A330NdLayout.cs`, `A330NdPage.cs`, `A330NdText.cs`, `Forms/IniA330/A330NdWindow.cs`; tests `A330NdPageTests.cs`, `A330NdTextTests.cs`, `A330NdWindowTests.cs`; fixtures `p09_nd_<mode>_<range>.json`.

**Interfaces:**
- Produces:
  ```csharp
  namespace MSFSBlindAssist.Aircraft.A330.Nd;
  internal sealed record A330NdItem(string Text, double DistanceNm, double BearingFromTop, string? Colour);
  internal sealed record A330NdPage(string Mode, int RangeNm, IReadOnlyList<string> TopLeft, IReadOnlyList<string> TopRight,
      IReadOnlyList<A330NdItem> Map, IReadOnlyList<string> Bottom, IReadOnlyList<string> Messages)
  {
      /// Null unless the frame is an ND's and complete. Mode and range come from the EFIS panel's variables.
      public static A330NdPage? From(A330Frame frame, int modeValue, int rangeValue);
  }
  ```

- [ ] **Step 1:** With the owner's route loaded and the IRS aligned, capture the captain's ND in each mode (ROSE LS, ROSE VOR, ROSE NAV, ARC, PLAN) at two ranges, with each option key on (CSTR, WPT, VOR.D, NDB, ARPT) and with each needle selected. In the evidence file: the corner areas' bounds; in each mode the aircraft symbol's place and the pixels a nautical mile at each range (two waypoints of known distance from the MCDU's F-PLN page fix the scale); what the top of the map is in each mode; the message line's place; every text seen and what it is.
- [ ] **Steps 1-5:** tests on the fixtures: `The_top_left_reads_speeds_and_wind`, `The_top_right_reads_the_next_waypoint_its_bearing_distance_and_time`, `A_map_waypoint_reads_distance_and_degrees_left_or_right_in_ARC_and_ROSE`, `In_PLAN_a_waypoint_reads_distance_and_compass_point_from_the_centre`, `A_message_is_read_from_the_message_line` ("GPS PRIMARY", "MAP NOT AVAIL"), `Items_are_listed_nearest_first`, `An_incomplete_frame_gives_no_page`, de-DE; rows as the app's other ND windows word them ("Top left: ...", "Top right: ...", "Map: KLO, 8.1 NM, 30 degrees left", "Message: ..." or "Messages: none"); commit `feat(a330): the ND's page model`.
- [ ] **Step 2:** `A330NdWindow : A330DisplayWindow` (title "A330 ND", list "Captain ND"); its keys (spec D9): mode, range, CSTR, WPT, VOR.D, NDB, ARPT, needle 1, needle 2, terrain on ND, each by its key in the map's Captain EFIS panel; Ctrl+Shift+R shows the first officer's ND with the first officer's keys. Alt+N opens it, or falls back to the AI read (spec D3). Tests: `A_mode_change_from_the_window_relists_without_a_word`, `Each_key_shows_its_state`.

### Task 5: ND messages and the next waypoint

**Files:** Create `Aircraft/A330/Nd/A330NdAnnouncer.cs`; modify `Aircraft/A330/A330CalloutRows.Nd.cs`, `Aircraft/IniA330Definition.FlightDisplays.cs`; test `A330NdAnnouncerTests.cs`.

- [ ] **Steps 1-5:** `A330CalloutRows.Nd` = `A330_CALLOUT_ND_MESSAGES` "ND messages", `A330_CALLOUT_ND_WAYPOINT` "Next waypoint". Tests: `A_new_message_is_spoken_once` ("ND message: GPS PRIMARY"), `A_message_gone_is_spoken_once`, `A_new_next_waypoint_is_spoken_with_its_distance` ("ND: RONII, 8.1 NM"), `The_first_reading_is_silent`, `A_muted_row_says_nothing`, `Messages_are_compared_only_between_agreeing_complete_frames`; the key that reads the next waypoint on the other aircraft (`ReadNDWaypoint`) says the top right corner as drawn, or "No active waypoint"; commit `feat(a330): ND messages and the next waypoint`.

### Task 6: The ISIS window

**Files:** Create `Aircraft/A330/Isis/A330IsisPage.cs`, `Forms/IniA330/A330IsisWindow.cs`; tests `A330IsisTests.cs`; fixture `p09_isis.json`.

- [ ] **Steps 1-5:** rows from the frame (reference on the ground: 16 texts, 39 characters): the speed at the tape's pointer, the altitude assembled from its drum digits, the altimeter setting as drawn ("1013/2992"), any flag or mode text as drawn; scale numbers of the tapes are not listed. Tests: `The_altitude_is_assembled_from_its_digits`, `The_setting_is_read_as_drawn`, `Tape_scale_numbers_are_left_out`, `Alt_I_opens_the_window`; the window's keys are the standby instrument's keys the map has; commit `feat(a330): the ISIS window`.

### Task 7 [sim] [owner]: The DCDU (M11)

**Files:** Create `Aircraft/A330/Dcdu/A330DcduPage.cs`, `A330DcduAnnouncer.cs`, `Forms/IniA330/A330DcduWindow.cs`; modify `Aircraft/A330/A330CalloutRows.Datalink.cs`; tests `A330Dcdu*Tests.cs`; fixtures `p09_dcdu_*.json`.

- [ ] **Step 1:** With the owner: bring a message onto the DCDU (the aircraft's own datalink through its ATC pages, with the owner's logon and go-ahead; nothing is sent to a live network without the owner saying so). Capture it empty, with an uplink, with a reply selected, and with two messages queued. Record the page's areas and the keys' effect (plan 04's DCDU keys).
- [ ] **Steps 1-5:** rows: the header, the message's lines, the reply options with the selected one marked "(selected)", the message count; no colour words. `A330CalloutRows.Datalink` = `A330_CALLOUT_DCDU` "DCDU messages". Tests: `An_uplink_is_spoken_once_on_arrival` ("DCDU: <header>, <first line>"), `Two_queued_messages_say_their_count`, `Opening_the_window_does_not_say_it_again`, `A_muted_row_says_nothing`, `The_window_carries_the_DCDUs_keys`; Ctrl+Shift+D opens `A330DcduWindow`; commit `feat(a330): the DCDU window and uplink call-outs`.
- [ ] **Step 2:** If no message can be brought up, the window ships listing "no message" with its keys, the call-out is left out, and the record says the DCDU's content was never measured.

### Task 8: The developer doc

- [ ] **Step 1:** "Flight modes" rewritten (the words are read from the PFD's band; the numbers only ask for a fresh frame and give today's words while no PFD frame comes; no table is extended from flights); a section a display for the ND, ISIS and DCDU (areas, scale, what is not read).

## In-sim checks (owner)

1. In flight (plan 11): every flight mode change is spoken in the words on the PFD; none is spoken as a number by the end of the flight.
2. Output Alt+N with a route: the next waypoint and three map items read where you expect them; change mode and range from the window.
3. Output Alt+I on the ground: speed, altitude and the altimeter setting.

## Handoff to plan 12

- **Pilot-facing:** the four windows and their keys; flight modes in the PFD's words; the ND and DCDU Ctrl+M rows; the next-waypoint key. Guide: "Special windows"; hotkey guide: Output Alt+P, N, I, Ctrl+Shift+D, the next-waypoint key. Changelog `feature`.
- **Rule proposals:** INI-20: a flight mode's word is read from the PFD's band (`A330FmaBand`); while no PFD frame comes the words are today's from the numbers; no word comes from another aircraft and no table of numbers to words is built from flights. INI-21: an ND map item is said by distance and direction computed from the measured scale of its mode and range.
- **For plan 11:** M10 (the band read through the whole flight, against the call-outs heard), M11 (the ND in flight at each range; a DCDU uplink if the owner wants one).

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
