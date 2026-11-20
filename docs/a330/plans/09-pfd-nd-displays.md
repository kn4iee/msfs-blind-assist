# A330 PFD, ND, ISIS and DCDU Implementation Plan (plan 09, later)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** The flight modes are spoken in the PFD's own words: every mode number's word is taken from what the PFD drew while that number was set. Output Alt+P, Alt+N, Alt+I and Ctrl+Shift+D open live PFD, ND, ISIS and DCDU windows, each with the cockpit controls that change it (spec D9). ND messages, the next waypoint and DCDU uplinks are called out. The PFD window lists the PFD's drawn text only if the PFD proves readable; if it does not, the plan says so and the window carries the flight modes and the values the aircraft publishes.

**Architecture:** The FMA keeps its source, the mode L:vars, which change at once. A recorder pairs each set of mode numbers with the texts the PFD drew in its mode band; each captured text is exact even when the frame is not complete, so the pairing needs no complete PFD frame. The ND, ISIS and DCDU are read as plan 08 reads the ECAM displays: a pure page model by measured position, a pure text step, plan 02's window with key rows, and announcers that compare complete frames.

**Tech Stack:** C# 13 / .NET 10 Windows Forms, xUnit; plan 02's `IA330Displays`, `A330DisplayWindow` and replay source; the reader program's `capture` and `bench` commands for the sim tasks.

**Spec:** `docs/a330/spec.md` sections 2.1, 2.7, 3 (R16, R17), 5 (D3, D7, D8, D9), 6 (M9, M10, M11); `docs/ini-a330.md` "Flight modes"; the Fenix's rules for the same displays as the wording yardstick (`.claude/rules/fenix-channel-displays.md`).

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply.
- Owned files: `Aircraft/IniA330Definition.FlightDisplays.cs`, `Aircraft/A330/A330Fma.cs`, `Aircraft/A330/Pfd/**`, `Nd/**`, `Isis/**`, `Dcdu/**` (new), `Aircraft/A330/A330CalloutRows.Pfd.cs`, `.Nd.cs`, `.Datalink.cs`, `Forms/IniA330/A330PfdWindow.cs`, `A330NdWindow.cs`, `A330IsisWindow.cs`, `A330DcduWindow.cs` (new), tests `tests/MSFSBlindAssist.Tests/A330/A330Fma*Tests.cs`, `A330Pfd*Tests.cs`, `A330Nd*Tests.cs`, `A330Isis*Tests.cs`, `A330Dcdu*Tests.cs`, fixtures `p09_*.json`; `docs/ini-a330.md` "Flight modes"; `docs/a330/evidence/09-displays.md` (new).
- A flight mode's word is one the PFD drew. A number with no word yet is spoken as today ("vertical mode 14") and logged; it is never given a word from another aircraft.
- Colour words follow spec D8's table, which follows the Fenix's windows. PFD: only the flight mode band's words say their colour ("Vertical: ALT, cyan"), in the window and in the call-outs; the tapes, values and flags say none. ND: amber and red always, any other colour only where it is unusual for that kind of item or tells two items apart ([FNX-41]'s rule; the usual colours of each kind of item are measured from the fixtures, as `FenixChannelNdSpeech` holds them). ISIS and DCDU: none.
- A map item's place is said as a distance and a direction from the aircraft, never as screen coordinates.
- Window keys press the panel's controls through `PressFromWindow` by their keys in plan 04's map (the EFIS panels exist today; the standby instrument's and the DCDUs' keys come with plan 04's Task 5: a window lists the keys the map has when it is built, and its test builds in-memory rows).

## Measured on 2026-11-20

- **Task 1 (M9), with the proof:** the PFD can be read, but not by one copy of the slow proof. One alone got 24 of
  30 items in 20 s and 34 of 36 in 40 s; eight run side by side got 30 of 31 within 2.5 s. The go or
  no-go waits for the built reader's own figure.
- **The flight modes are text** (Task 2 Step 1, started): at the gate with the captain's flight director
  on, the armed row read ALT and NAV in cyan, with `1`, the FD symbol and `-` at the right; with it off
  the band is empty. The speed tape drew V1 in cyan and V2 in magenta.
- The PFD shows only a window of each tape: the frame holds the numbers on the screen.
- **Self test:** SELF TEST IN PROGRESS / MAX 40 SECONDS, as text, on the PFD and the ND. Before the IRS
  align the PFD draws no text at all and the ND reads HDG and MAP NOT AVAIL in red.
- **Task 4, the ND:** with the IRS aligned and no route, complete in 1 s (KATL, the nearest navaid, the
  cross-track figure, GPS PRIMARY). **With a route at 20 and 80 NM it was NOT read complete** (at 10 NM it was: see below) in any mode (ROSE LS, VOR,
  NAV, ARC, PLAN at 20 and 80 NM): at best 7 of 13 items in 10 s with eight copies side by side. What did come was
  right (the runway, INTCPT, SNUFY, SLAWW, RIGGZ, RIVTT, the cross-track figure). This is a second go or
  no-go, on the built reader, before Task 4's window promises the route's items; the next-waypoint
  read-out (Task 5) must not depend on it.
- A mode or range change draws a message on the ND for a moment.

Later the same day, on the owner's own preflight (the plan uplinked from the INIT page, flight directors
on, IRS aligned during the session):

- **IRS off:** the PFD draws no text at all; the ND draws HDG and MAP NOT AVAIL in red and, top right,
  the next leg all the same (its name, course and distance: "1530", "106", "0.8 NM").
- **PFD, IRS aligned, flight directors on:** 38 items; eight copies of the proof read 37 of them, one
  alone 28 to 32 in 12 s. In the mode band: "V/S" in green with its value in cyan beside it, and "1",
  the FD symbol and "2" at the right (both flight directors). On the speed tape V1 in cyan and V2 in
  magenta (142 and 148 that day). An amber X twice (drawn where a value is not valid). QNH and its
  value, the altitude and heading tapes' numbers.
- **ND, ARC at 10 NM, the uplinked route, IRS aligned: complete**, 16 items, and complete for each copy
  of the proof alone too: ground speed and "---" true airspeed top left, the wind as "---/---", the next
  leg top right, KATL and 09L by the runway, the cross-track figure "0.2L", a navaid in cyan, the next
  waypoint, GPS PRIMARY at the bottom. So the ND is not unreadable with a route: the failures above were
  at 20 and 80 NM with many more waypoints on the map. The go or no-go stays, but the question is now
  "how many map items at once", to be measured at each range.
- **Text outside the display.** The ND drew a waypoint behind the aircraft (LIDAS) at a position below
  the bottom edge of the display, with no clip set. The frame carries it; Task 4's map list drops any
  item whose position lies outside the display's own area (its size is a named constant of `A330NdLayout`, from
  the fixtures), or it would name waypoints a sighted pilot cannot see.
- The degree sign on the ND is the font's `!`; the FD symbol on the PFD is the letters `fd` in its font:
  each display's special characters get a table of their own, as the MCDU's have (plan 03).
- **ISIS:** complete, including its flags and the count-down while it aligns ("INIT 53S").
- **DCDUs:** nothing drawn while empty, and the two cannot be told apart yet (Task 7).

## Review Focus

- A mode word paired with the wrong number because the PFD was drawn a frame before the L:vars changed (or after): a pair counts only when the same numbers and the same band texts were seen together in three samples at least 200 ms apart (Task 2 test).
- Two modes drawn in one column (an active and an armed line): the recorder keeps them by their line, never joined (Task 2).
- An ND waypoint's direction said from the screen's top in a mode whose top is not the aircraft's heading (PLAN mode is north up): the direction rule is per mode (Task 4 test for each mode).
- A DCDU message spoken twice, once on arrival and again when the pilot opens the window: spoken once per arrival (Task 7 test).
- The first officer's ND and PFD showing something else than the captain's: each window reads its own gauge (tests on two fixtures).

---

### Task 0 [controller]: Claim this plan

As plan 01's Task 0, with this file's path and `docs(a330): claim plan 09` (`Plan: 09`). Start from the branch once plan 02 has landed.

### Task 1 [sim]: Can the PFD be read? (M9)

**Files:** the evidence file.

- [ ] **Step 1:** With the IRS aligned, the reader program's `bench PfdCaptain 60` on the ground and (from plan 11, or a short flight with the owner) in the air: how often a complete frame arrives within 2 s, over 50 tries, and how many of the PFD's texts a frame holds when it is not complete. The reference from the proof: a slow script caught 1 text in 7 s.
- [ ] **Step 2:** Capture what was caught as `p09_pfd_*.json` and list in the evidence file which of the PFD's texts are text (speeds, altitudes, the mode band, messages) and which never appear (drawn as symbols: the self test showed its flags are not text).
- [ ] **Step 3:** The decision, recorded with its numbers: the PFD window lists drawn text only if a complete frame arrives within 2 s in at least 9 of 10 tries. Otherwise Task 3 builds the window without it, and the record says so.

### Task 2: The flight modes in the PFD's own words (R17, M10)

**Files:** Create `Aircraft/A330/Pfd/A330FmaBand.cs`, `A330FmaWords.cs`; modify `Aircraft/A330/A330Fma.cs`, `Aircraft/IniA330Definition.FlightDisplays.cs`; tests `tests/MSFSBlindAssist.Tests/A330/A330FmaWordsTests.cs`, `A330FmaTests.cs`.

**Interfaces:**
- Produces:
  ```csharp
  namespace MSFSBlindAssist.Aircraft.A330.Pfd;
  /// The PFD's mode band from whatever texts a frame holds (the frame need not be complete): each text with its column 1..5 and line 1..3.
  internal static class A330FmaBand { public static IReadOnlyList<(int Column, int Line, string Text, string Colour)> From(A330Frame frame); }
  /// Pairs mode numbers with the band's texts.
  internal sealed class A330FmaWords
  {
      public void Add(A330FmaInputs numbers, IReadOnlyList<(int Column, int Line, string Text, string Colour)> band, long nowTicks);
      /// The word seen for this column's number, once the same numbers and texts were seen together in three samples at least 200 ms apart; else null.
      public string? WordFor(string column, int number);
      public IReadOnlyList<string> NewPairs();     // "vertical 14 = OP DES" lines not reported yet, for the log
  }
  ```
  The band's geometry (which strip of the PFD, the five columns' x ranges, the three lines' baselines) is measured in Step 1 and held in `A330FmaBand` as named constants with the fixture that proves them.

  Colour in a call-out (spec D8, as the Fenix's "FMA: Vertical: LAND, green"): a flight mode call-out is still spoken from the mode numbers the moment they change (D7) and is never held back for the PFD. It adds the colour only when the newest band read already shows that same word; otherwise it speaks the word as today ("armed" stays, from the numbers). Tests: `A_call_out_says_the_colour_when_the_band_shows_the_word`, `A_call_out_never_waits_for_the_band`.

- [ ] **Step 1 [sim]:** On the ground, with the owner's go-ahead: set flight directors on, and capture the band as each ground-reachable mode shows (the six numbers already measured, CLB armed, a heading or track selection, an ALT selection); record the band's geometry and each (numbers, texts) pair in the evidence file.
- [ ] **Steps 1-5:** tests: `A_pair_seen_three_times_200_ms_apart_is_a_word`, `A_pair_seen_once_is_not`, `Numbers_that_changed_between_two_samples_start_again`, `An_armed_line_is_kept_apart_from_the_active_line`, `A_number_with_two_different_texts_keeps_neither_and_logs_both`, `The_band_ignores_texts_outside_its_strip`; implement: while the A330 is active the definition wants `PfdCaptain` at the background refresh, feeds `Newest` (complete or not) and the current mode numbers to the recorder, writes each new pair to `fma.log`, and where `A330Fma` has no word for a number speaks the recorder's word when it has one; commit `feat(a330): flight mode words from the PFD`.
- [ ] **Step 2 [in flight]:** After plan 11's flight, every pair in its `fma.log` goes into `A330Fma`'s tables with the flight's date, replacing each word that was a guess; a number the flight never showed stays a number. `A330FmaTests` gets one case a pair. Until then this plan is "waiting on plan 11" for this step only.

### Task 3: The PFD window

**Files:** Create `Aircraft/A330/Pfd/A330PfdText.cs`, `Forms/IniA330/A330PfdWindow.cs`; modify `Aircraft/IniA330Definition.FlightDisplays.cs`; tests `A330PfdTextTests.cs`, `A330PfdWindowTests.cs`.

- [ ] **Steps 1-5:** rows: the flight modes by column ("Thrust: ...", "Vertical: ..., armed ...", "Lateral: ...", "Approach capability: ...", "Autopilot: ...") from `CurrentFma()`, each mode word followed by the colour the band drew it in when Task 1's decision gives the band ("Vertical: V/S, green; ALT, cyan"), worded as the Fenix's flight mode rows are, and with no colour when the band is not read; no other PFD row says a colour (tests `A_mode_word_says_the_bands_colour`, `No_tape_value_or_flag_says_a_colour`, `Modes_say_no_colour_when_the_band_is_not_read`); then, only if Task 1's decision allows, the PFD's drawn texts in screen order (plan 08's rule: as drawn, named only by a drawn label, else with their place); otherwise the speed, altitude, heading, vertical speed and altimeter setting from the variables the read-out keys use, and one row "PFD text not read on this aircraft". Tests: `The_modes_come_first_by_column`, `Drawn_text_is_listed_only_when_the_decision_allows`, `Alt_P_opens_the_window_and_falls_back_to_the_AI_read_when_the_reader_is_not_running`, `The_window_carries_the_captains_FD_LS_and_altimeter_controls`, de-DE; `A330PfdWindow : A330DisplayWindow` (title "A330 PFD", list "PFD display"); commit `feat(a330): the PFD window`.

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

- [ ] **Step 1:** "Flight modes" rewritten (the numbers stay the source, the words are the PFD's, how a pair is recorded, where the log is); a section a display for the ND, ISIS and DCDU (areas, scale, what is not read).

## In-sim checks (owner)

1. In flight (plan 11): every flight mode change is spoken in the words on the PFD; none is spoken as a number by the end of the flight.
2. Output Alt+N with a route: the next waypoint and three map items read where you expect them; change mode and range from the window.
3. Output Alt+I on the ground: speed, altitude and the altimeter setting.

## Handoff to plan 12

- **Pilot-facing:** the four windows and their keys; flight modes in the PFD's words; the ND and DCDU Ctrl+M rows; the next-waypoint key. Guide: "Special windows"; hotkey guide: Output Alt+P, N, I, Ctrl+Shift+D, the next-waypoint key. Changelog `feature`.
- **Rule proposals:** INI-20: a flight mode's word comes only from a pair the PFD showed (`A330FmaWords`), never from another aircraft; an unpaired number is spoken as a number. INI-21: an ND map item is said by distance and direction computed from the measured scale of its mode and range.
- **For plan 11:** M10 (the `fma.log` of the whole flight), M11 (the ND in flight; a DCDU uplink if the owner wants one).

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
