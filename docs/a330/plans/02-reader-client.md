# A330 Display Reader Client Implementation Plan (plan 02, early)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** The app's side of the display reader: the contract (a display's text frame), a client that starts the reader program beside the app and receives its frames, the reader's status in the pilot's words, one spoken notice when the displays cannot be read, the shared live display window, and a replay source so every later plan is built and tested on captured frames with no simulator.

**Architecture:** The reader is a separate program, `A330DisplayReader.exe`, built from the team's private reference and placed beside the app, as the layout generator is. This repository holds none of it and nothing about how it reads. The app starts it without a window, talks to it over its standard input and output in a small line protocol, and ends it when the app ends. Everything downstream sees only `IA330Displays`: frames of drawn text with positions, fonts and colours, each marked complete or not.

**Tech Stack:** C# 13 / .NET 10 Windows Forms, xUnit; `System.Diagnostics.Process`; the app's `KillOnCloseJob`.

**Spec:** `docs/a330/spec.md` sections 2.1, 3 (R4, R5), 4 ("The display reader"), 5 (D1 to D3, D14, D15), 6 (M14, M15). The reader program's own plan is in the private reference.

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply.
- Owned files: `Aircraft/A330/Display/**` (new), `Aircraft/IniA330Definition.Displays.cs` (new), `MainForm.IniBuilds.Displays.cs` (new), `Forms/IniA330/A330DisplayWindow.cs` (new), tests `tests/MSFSBlindAssist.Tests/A330/A330Reader*Tests.cs`, `A330Display*Tests.cs`, `A330ColoursTests.cs`, `A330ReplayDisplays.cs` (the test project's replay source), fixtures `tests/MSFSBlindAssist.Tests/A330/Fixtures/p02_*.json`; `docs/ini-a330.md` "Displays" (new section); `docs/a330/evidence/02-reader-client.md` (new).
- **Nothing in this repository reads the A330's displays itself, and no public text says how the reader works.** The app only receives frames from the reader program. Code, comments, tests, docs and commit messages speak only of "the display reader" and "frames".
- The protocol below is the contract with the reader program. A change to a line both sides read is agreed with the reader plan's claimant first, and `A330ReaderProtocol.Version` goes up.
- A fixture is a frame in the protocol's form: drawn text, positions, fonts, colours. Nothing else.
- **Colours and hidden list rows (spec M14, M15).** The reader does not give them yet. The 2026-11-20 captures (colours given, hidden rows left out) stand for what it will give once both are solved: plans 03, 08 and 09 build and test on those fixtures, and the current reader's captures replace them when they exist. At run time a frame with no colour gets no colour word and drives no call-out that needs a colour; a frame whose hidden rows are not left out (`HiddenRowsLeftOut` false) shows and speaks nothing that could be a hidden row. A window says what it is waiting for ("Colours not read yet", "List rows not read yet") in its status row. Shipping: plan 12 checks M14 and M15, or the owner's ruling to ship without.
- Nothing on the client's reading thread touches SimConnect, a control or the announcer (SIM-5, SIM-20, A380C-6): consumers marshal.
- The reader's own log lines (its standard error) go to a named log through `Log.Channel("a330-displays")` (CORE-14, CORE-15).

## Changed on 2026-11-21

The reader's approach changed (spec section 2.1, D14, D15). What it means for this plan:

- **Every display reads whole**, the PFD and the ND with a route included; no window needs a fallback
  for a display that cannot be read whole.
- **The first time in each simulator session the reader takes a while to start** (72 s in the proof; the
  built reader must cut it, M1). The state is `Starting` all that time: a passing state, never spoken,
  shown in the windows as "A330 displays starting". Opening BA again with the simulator still running
  does not wait again.
- **Every refresh brings a frame.** The reader sends each wanted display's frame every refresh, changed
  or not, so `Latest` stays current on a steady page and two frames in a row can agree. A wanted display
  the reader has not found yet sends no frame: its window says "Reading the <display>" in its status row,
  and after 30 s "The display reader cannot read the <display>" (Task 5 tests
  `A_display_with_no_frame_says_reading`, `After_30_seconds_with_no_frame_the_window_says_it_cannot_read_it`).
- **Refresh:** the app asks 250 ms while a window shows a display and 1000 ms in the background. R4's
  3 seconds (D15): a call-out needs two agreeing frames, so in the background a change is spoken within
  about 2 s plus the reader's own time.
- **Colour is optional** in the contract (`A330Text.Colour` null; the `colour` key left out on the wire)
  until the reader gives colours (M14). `A330Colours.Name` is never called with none; tests
  `A_text_with_no_colour_has_no_colour_word` (in `A330ColoursTests`), `A_frame_line_without_colour_parses`
  (in `A330ReaderProtocolTests`).
- **A frame says whether hidden list rows were left out:** `A330Frame.HiddenRowsLeftOut`, false unless
  the frame line carries `"hiddenRowsLeftOut":true`, which the reader sends only once M15 is solved. Test
  `A_frame_line_without_hiddenRowsLeftOut_reads_false` (in `A330ReaderProtocolTests`). The bullets of
  2026-11-20 below are the first approach's.

## Measured on 2026-11-20

One ground session, cold and dark to a programmed MCDU, with the first approach's proof (spec section 6).
What it meant for this plan, where 2026-11-21 above does not replace it:

- **A display the aircraft is not drawing gives a frame with `Drawn` false** (Task 1's record and Task 2's
  line carry it since this date). Cold and dark, no display shows anything. The window says "<display>
  is off"; the notice never calls it a
  failure.
- **(First approach.) A frame held what is on the screen.** The aircraft draws a whole list and shows a
  window of it; with the first approach the rows outside the window never reached the app. With the
  current one they can, until M15 (above).
- **Paused, and with a display dimmed to zero, frames keep coming** unchanged. Nothing in the client may
  stop asking because the simulator is paused or a brightness reads 0.
- **(First approach.) Times with the slow proof:** an MCDU page 0.5 to 3 s, the E/WD 2 to 9 s, a lower
  ECAM page 1.5 to 6 s. Not the refresh defaults any more (2026-11-21 above).

## Review Focus

- The program is not beside the app (a build without it): the state is `NotInstalled` from the start, nothing is retried, and the pilot hears one notice, not one a window (Task 3, Task 5).
- The program dies in flight: the client restarts it, a few times, and the last complete frame is never shown as current after three refreshes (Task 3 tests).
- A reader of another protocol version: refused as `OutOfDate`, never half-understood (Task 2 test).
- A line that is not valid JSON, or a frame naming an unknown gauge: dropped and logged once, never an exception on the reading thread (Task 2 tests).
- The app exits or crashes: the program ends with it (the job object), and a second app instance starts its own (Task 3 test).
- A number in a frame parsed under a comma-decimal culture (Task 2 test under de-DE and sv-SE).

---

### Task 0 [controller]: Claim this plan

As plan 01's Task 0, with this file's path and `docs(a330): claim plan 02` (`Plan: 02`). Branch from the base commit the index records for early plans.

### Task 1: The contract

**Files:** Create `Aircraft/A330/Display/A330Frame.cs`, `A330Colours.cs`, `IA330Displays.cs`, `A330ReaderState.cs`; tests `tests/MSFSBlindAssist.Tests/A330/A330ColoursTests.cs`, `A330ReaderStatusTests.cs`.

**Interfaces:**
- Produces (what plans 03, 08, 09 and 10 consume):
  ```csharp
  namespace MSFSBlindAssist.Aircraft.A330.Display;
  public enum A330Gauge { McduCaptain, McduFirstOfficer, Ewd, Sd, PfdCaptain, PfdFirstOfficer, NdCaptain, NdFirstOfficer, DcduCaptain, DcduFirstOfficer, Isis }
  public readonly record struct A330Rgb(float R, float G, float B);
  /// One text as the display drew it. X is where its first character starts, Baseline its text line, in the display's own pixels;
  /// CharX is each character's start; Order is its place in the display's drawing order.
  /// Colour is null while the reader gives none (spec M14: not found yet with its current approach); a consumer then says no colour word.
  public sealed record A330Text(int Order, float X, float Baseline, string Text, string Font, float Size, A330Rgb? Colour, IReadOnlyList<float> CharX);
  /// One frame of one display. Complete: the reader vouches that no text the display drew is missing.
  /// Missing: how many characters the frame lacks (0 when complete).
  /// Drawn is false when the display shows nothing (the cockpit unpowered). Such a frame has no texts and is not complete;
  /// it is a state, not a failure.
  /// HiddenRowsLeftOut is true only when the reader vouches that rows the display does not show (the rest of a list) are left
  /// out; false until spec M15 is solved. On the wire "hiddenRowsLeftOut":true, absent meaning false.
  public sealed record A330Frame(A330Gauge Gauge, IReadOnlyList<A330Text> Texts, bool Complete, int Missing, long ReadTicks, bool Drawn = true, bool HiddenRowsLeftOut = false);

  /// A drawn colour's word, by hue: "white", "grey", "red", "amber", "yellow", "green", "cyan", "blue" or "magenta".
  /// Seen on the aircraft: MCDU amber (0.98, 0.76, 0.29), cyan (0.31, 0.82, 1.00), greyed prompt (0.30, 0.30, 0.30); ECAM amber (1.00, 0.55, 0.00),
  /// green (0.38, 0.80, 0.39), cyan (0.40, 0.86, 1.00); ISIS green (0.46, 1.00, 0.46); ND flag red (1.00, 0.00, 0.00).
  public static class A330Colours { public static string Name(A330Rgb colour); }

  public enum A330ReaderState { NotInstalled, Starting, Running, SimulatorNotFound, NoAccess, AircraftNotFound, NotRecognised, OutOfDate, Failed, Stopped }
  public static class A330ReaderStatus
  {
      public static string Text(A330ReaderState state);     // the pilot's words; empty while Running
      public static bool IsLasting(A330ReaderState state);  // true for NotInstalled, NoAccess, NotRecognised, OutOfDate, Failed
  }

  public interface IA330Displays
  {
      A330ReaderState State { get; }
      string StatusText { get; }                                 // A330ReaderStatus.Text(State)
      void Want(A330Gauge gauge, object owner, int refreshMs);   // read this display about every refreshMs until Release
      void Release(A330Gauge gauge, object owner);
      A330Frame? Latest(A330Gauge gauge);                        // the newest COMPLETE frame no older than three refreshes; else null
      A330Frame? Newest(A330Gauge gauge);                        // the newest frame, complete or not
      string? McduScratchpad(bool firstOfficer);                 // the MCDU's scratchpad text as the reader gives it; null when it gives none
      event Action<A330Gauge>? FrameChanged;                     // on the client's thread: a complete frame differs from the last complete one
      event Action<A330ReaderState>? StateChanged;               // on the client's thread
  }
  ```
  Wording: `NotInstalled` "A330 displays unavailable: the display reader is not installed"; `Starting` "A330 displays starting"; `SimulatorNotFound` and `AircraftNotFound` "A330 displays waiting for the aircraft"; `NoAccess` "A330 displays unavailable: no access to the simulator"; `NotRecognised` "A330 displays unavailable: this simulator version is not recognised yet"; `OutOfDate` "A330 displays unavailable: the display reader is out of date"; `Failed` "A330 displays unavailable: the display reader stopped"; `Stopped` "A330 displays stopped".

- [ ] **Steps 1-5:** tests: `Each_seen_colour_has_its_word` (a theory over the colours above, plus pure white, and equal components above and below 0.5 for white and grey), `Every_state_has_words_and_Running_has_none`, `The_lasting_states_are_the_five_named`, each wording under de-DE; implement; commit `feat(a330): the display frame contract`.

### Task 2: The protocol

**Files:** Create `Aircraft/A330/Display/A330ReaderProtocol.cs`; test `tests/MSFSBlindAssist.Tests/A330/A330ReaderProtocolTests.cs`; fixtures `p02_mcdu_init.json`, `p02_ewd_ground.json`.

**Interfaces:**
- Produces:
  ```csharp
  internal static class A330ReaderProtocol
  {
      public const int Version = 1;
      public static string Hello();                                        // {"hello":1}
      public static string Want(A330Gauge gauge, int refreshMs);           // {"want":"McduCaptain","refreshMs":250}
      public static string Release(A330Gauge gauge);                       // {"release":"McduCaptain"}
      public static string Reset();                                        // {"reset":true}
      public static string Quit();                                         // {"quit":true}
      /// One line from the reader: exactly one of the four is set. Null for a line that is not one of them.
      public static A330ReaderLine? Parse(string line, long nowTicks);
      public static A330Frame? ParseFrame(string json, long nowTicks);     // a fixture file's content
  }
  internal sealed record A330ReaderLine(int? HelloVersion, string? ReaderVersion, A330ReaderState? State, A330Frame? Frame, (A330Gauge Gauge, string Text)? Scratchpad);
  ```
  The reader's lines, one JSON object a line, UTF-8: `{"hello":1,"reader":"<version>"}`; `{"state":"Running"}` (the state names of `A330ReaderState` the reader can know: Starting, Running, SimulatorNotFound, NoAccess, AircraftNotFound, NotRecognised, Stopped); `{"frame":{"gauge":"McduCaptain","drawn":false,"complete":false,"missing":0,"texts":[]}}` for a display the aircraft is not drawing (`drawn` is left out when true); `{"frame":{"gauge":"McduCaptain","complete":true,"missing":0,"texts":[{"order":6,"x":22,"baseline":43,"text":"          INIT","font":"FMGSfont_A","size":29.5,"colour":[1,1,1],"charX":[22,39]}]}}`; `{"scratchpad":{"gauge":"McduCaptain","text":"TQXZ"}}`. Numbers are invariant.

- [ ] **Steps 1-5:** tests: `Each_request_is_one_line_of_the_documented_form`, `A_frame_line_parses_into_a_frame`, `A_state_line_parses`, `A_scratchpad_line_parses`, `A_hello_with_its_version_parses`, `An_unknown_gauge_or_state_gives_null`, `A_broken_line_gives_null_and_never_throws`, `Numbers_parse_the_same_under_de_DE_and_sv_SE`, `The_two_fixtures_parse` (the captain's MCDU on INIT has 18 texts whose characters sum to 146; the E/WD on the ground has 18 texts and 64 characters); the two fixtures come from the reader program's `capture` command (its plan's claimant supplies them, or the owner runs it); implement with `System.Text.Json`; commit `feat(a330): the display reader's protocol`.

### Task 3: The client

**Files:** Create `Aircraft/A330/Display/A330ReaderClient.cs`, `A330ReaderTransport.cs`; test `tests/MSFSBlindAssist.Tests/A330/A330ReaderClientTests.cs`.

**Interfaces:**
- Produces:
  ```csharp
  internal interface IA330ReaderTransport : IDisposable
  {
      void WriteLine(string line);
      event Action<string>? LineReceived;      // one of the reader's lines
      event Action? Exited;
  }
  internal sealed class A330ReaderProcess : IA330ReaderTransport
  {
      public const string FileName = "A330DisplayReader.exe";
      /// The program beside the app's exe; null when it is not there.
      public static string? NextToApp();
      public static A330ReaderProcess? Start(string path);     // no window, redirected input, output and error, joined to a KillOnCloseJob
  }
  internal sealed class A330ReaderClient : IA330Displays, IDisposable
  {
      public A330ReaderClient(Func<IA330ReaderTransport?> start, Func<long> nowTicks);
      public void Start();          // NotInstalled when `start` gives null
      public void Reset();          // a flight reload: sends reset and forgets every frame
      public void Stop();           // sends quit; disposes the transport after one second
  }
  ```
  Behavior: `Start` sends `hello`; a `hello` back with another version sets `OutOfDate` and stops; no `hello` within 5 s sets `Failed`. `Want` keeps the owners of each gauge and sends the smallest `refreshMs` among them; `Release` of the last owner sends `release`. A frame replaces the gauge's newest; a complete one also its latest, and raises `FrameChanged` when its texts differ from the latest before it. `Latest` returns null once the latest is older than three of the gauge's refreshes. A scratchpad line sets that side's text. `Exited` while not stopping: every frame is forgotten, state `Starting`, and the program is started again after 5 s, at most three times in five minutes, then `Failed`. After a restart every wanted gauge is asked for again.

- [ ] **Steps 1-5:** tests with a fake transport and clock: `No_program_is_NotInstalled_and_nothing_is_retried`, `A_hello_of_another_version_is_OutOfDate`, `No_hello_in_five_seconds_is_Failed`, `Two_owners_of_one_gauge_send_one_want_with_the_smaller_refresh`, `The_last_release_sends_release`, `A_complete_frame_becomes_latest_and_raises_FrameChanged_once`, `The_same_frame_again_raises_nothing`, `An_incomplete_frame_is_newest_but_not_latest`, `Latest_goes_null_after_three_refreshes`, `An_exit_restarts_and_asks_for_the_wanted_gauges_again`, `A_fourth_exit_in_five_minutes_is_Failed`, `Reset_forgets_every_frame_and_sends_reset`, `Stop_sends_quit`; implement; commit `feat(a330): the display reader client`.

### Task 4: The definition's part and the notice

**Files:** Create `Aircraft/IniA330Definition.Displays.cs`, `MainForm.IniBuilds.Displays.cs`, `Aircraft/A330/Display/A330ReaderNotice.cs`; test `tests/MSFSBlindAssist.Tests/A330/A330ReaderNoticeTests.cs`, `A330DisplaysLifecycleTests.cs`.

**Interfaces:**
- Consumes: plan 01's `ResetReader`, `StopReader`, `StartIniA330Displays`.
- Produces: `internal IA330Displays Displays { get; }` on `IniA330Definition` (a client that reports `NotInstalled` or `Stopped` until `MainForm` starts it; tests set a replay source through an internal setter); `internal sealed class A330ReaderNotice { public string? Next(A330ReaderState state, long nowTicks); }` (pure).

- [ ] **Steps 1-5:** tests: `A_lasting_state_is_spoken_once_thirty_seconds_after_the_aircraft_became_active` ("A330 displays unavailable: the display reader is not installed. The MCDU window cannot read its screen."), `A_passing_state_is_never_spoken` (Starting, waiting for the aircraft), `Running_after_a_spoken_notice_says_A330_displays_ready_once`, `A_new_lasting_reason_is_spoken_once`, `Switching_away_stops_the_reader`, `A_flight_reload_resets_it`, `Starting_twice_starts_one_program`; implement: `StartIniA330Displays` starts the client when the active aircraft is the A330 and stops it otherwise; the definition speaks `A330ReaderNotice`'s lines through the non-interrupting announce from a UI timer; commit `feat(a330): start the display reader with the aircraft and say when it cannot read`.

### Task 5: The live display window and the replay source

**Files:** Create `Forms/IniA330/A330DisplayWindow.cs`; `tests/MSFSBlindAssist.Tests/A330/A330ReplayDisplays.cs`, `A330DisplayWindowTests.cs`.

**Interfaces:**
- Consumes: plan 01's `PressFromWindow`, `DescribeControl`.
- Produces:
  ```csharp
  internal sealed record A330KeyRow(string VarKey, double? PressValue = null);   // a button (PressValue) or a choice list (null: the control's positions)
  internal abstract class A330DisplayWindow : Form
  {
      protected A330DisplayWindow(string title, string listName, IniA330Definition aircraft, SimConnectManager sim, ScreenReaderAnnouncer announcer, A330Gauge gauge, int refreshMs = 250);
      protected abstract IReadOnlyList<string> Rows(A330Frame frame);        // the list's lines, top of the display first
      protected virtual IReadOnlyList<A330KeyRow> Keys => [];                // the cockpit controls under the list (spec D9)
  }
  // test project
  internal sealed class A330ReplayDisplays : IA330Displays
  {
      public static A330ReplayDisplays FromFixtures(params string[] fixtureNames);
      public void Show(A330Frame frame);  public void SetState(A330ReaderState state);  public void SetScratchpad(bool firstOfficer, string? text);
  }
  ```
  The window: a `DisplayListBox` named `listName`, filled from `Rows(Latest)` through `DisplayList.UpdateInPlace` every `refreshMs`; it wants its gauge while visible and releases it when hidden; one row with `StatusText` when there is no complete frame and the state is not `Running`, "<display> is off" when the newest frame says it is not drawn (measured 2026-11-20: cold and dark, no display draws anything), or "reading" otherwise; F5 refreshes without a word; Escape closes and returns to the window before; under the list one control a `A330KeyRow`, labelled from `DescribeControl` ("STS: on"), pressed through `PressFromWindow`; nothing is spoken on open, refresh or close.

- [ ] **Steps 1-5:** tests on `A330ReplayDisplays`: `The_window_lists_the_frames_rows_and_updates_in_place`, `It_wants_its_gauge_while_visible_and_releases_it_when_hidden`, `The_status_row_shows_while_the_reader_is_not_running`, `Reading_shows_while_running_with_no_complete_frame`, `A_key_row_press_goes_through_PressFromWindow`, `A_key_rows_label_carries_the_controls_state`, `F5_and_open_say_nothing`; implement; commit `feat(a330): the live display window`.

### Task 6 [sim]: The client against the real program

**Prerequisite:** the reader program exists (its plan in the private reference) and a build of it is beside a Debug build of the app.

- [ ] **Step 1:** With the A330 loaded: the client reaches `Running`; a throwaway test window on `McduCaptain` lists the page; close the app and check in Task Manager that the reader went with it; start the app twice and check each has its own.
- [ ] **Step 2:** Rename the program away: `NotInstalled`, the notice once after 30 s, no process started. Put an older build beside the app if one exists: `OutOfDate`.
- [ ] **Step 3:** End the reader in Task Manager while a window is open: the status row shows within three refreshes, the reader comes back by itself, and the list fills again without a word.
- [ ] **Step 4:** Record in the evidence file the refresh each display's owner should ask for (from the reader plan's measurements) and set the defaults.

## In-sim checks (owner)

1. Start BA with the A330 loaded: no notice is spoken, and the MCDU window (plan 03) reads within a second once "A330 displays starting" has gone (the first time in a simulator session the reader starts first).
2. Close BA: the simulator carries on, and no reader program is left running.

## Handoff to plan 12

- **Pilot-facing:** the notice and the status wording, for the guide's "Things to know". Nothing else by itself.
- **Rule proposals:** INI-10: nothing in this repository reads the A330's displays itself; display text comes only from the reader program through `A330ReaderClient`, and public text never says how the reader works. INI-11: only a frame with `Complete` true is shown or spoken (`IA330Displays.Latest`); `Newest` is for pairing single texts and for saying "reading", never for a page. INI-12: a fixture is a frame in the protocol's form and nothing else.
- **Frozen files:** the release and preview workflows build the reader program and place it beside the app (plan 12's Task 2); `docs/updates.md` if it lists what a release carries.

## Execution record

Kept in this file only, so no other plan's push ever touches it.

- **Status:** claimed
- **Claimed by:** kn4iee
- **Worked by:** kn4iee, with Claude (subagent-driven development)
- **Started:** 2026-11-22, from b9aa5d0a3 (the branch tip: e5c9d2b6a plus plan 01's two record commits)
- **Landed:**
- **Tests at landing:**
- **Tasks:**
- **Waiting on the owner:**
- **In-sim results:**
- **Changed from the plan:**
- **Rulings made while working:**
