# BlueBird 757 MCP Implementation Plan (plan 05, early: essentials)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** The 757's MCP windows have fixed Alt keys like the PMDG windows, Speed Intervene (and Altitude Intervene if BlueBird models it), a speed box that is closed while VNAV blanks the window, Mach entry and readout, and the MCP values, A/T arm and the disconnect lights announced when they change. (A fuller Ctrl+P window is plan 14, which takes the autopilot window files over.)

**Architecture:** The four windows keep the HS787's `ValueInputForm` (modeless, Enter sets, read-back spoken). The button lists move from "first letter of the last word" to a fixed key table. Mach reuses the speed set's burst machinery with the Mach step measured on the owner's flight. New announcements watch BlueBird's own variables through new Continuous keys (never stock autopilot variables), speak once a value has rested, and stay silent for the app's own sets.

**Tech Stack:** C# 13 / .NET 10 Windows Forms, xUnit.

**Spec:** `docs/b757/spec-gaps.md` sections 1 (#8, #9, #10), 3.4, 4 (R57-R60, R68, R69), 8; `docs/b757.md` "The MCP value windows", "The autopilot window"; rule B757-3.

## Global Constraints

- Owned files (`Bbs757AutopilotLayout.cs`, `BBS757AutopilotWindow.cs`, `BBS757Definition.Autopilot.cs` and `Bbs757McpWindowTests.cs` then passed to plan 14): `Aircraft/BBS757/Bbs757Mcp.cs`, `Bbs757McpWindowLayout.cs`, `Bbs757AutopilotLayout.cs`, `Aircraft/BBS757/Bbs757Mach.cs` (new), `Aircraft/BBS757/Bbs757AutoflightAnnouncements.cs` (new), `Aircraft/BBS757Definition.Mcp.cs`, `Aircraft/BBS757Definition.Autopilot.cs`, `Forms/BBS757/BBS757AutopilotWindow.cs`, tests `Bbs757McpTests.cs`, `Bbs757McpWindowTests.cs` (made by plan 01 from moved tests), `Bbs757MachTests.cs` (new), `Bbs757AutoflightAnnouncementsTests.cs` (new), `docs/b757/evidence/flight-05.md`.
- BlueBird variables: speed intervention `L:B757_MCP_IAS_MACH_KNOB_BUTTON` (written by node `B757_MCP_AP_SPD_HOLD_SW`); IAS/MACH changeover node `B757_MCP_AP_SPD_MACH_SW`; Mach flag `L:BBS_AIRSPEED_IN_MACH`; window blank `L:BBS_IAS_BLANKED` (1 blank); Mach target `L:BBS_TARGET_MACH`; IAS target `L:BBS757_ORANGE_AIRSPEED_BUGS`; `L:BBS_AP_TARGET_HEADING`, `_ALTITUDE`, `_VS`; A/T armed `L:BBS_AUTO_THROTTLE`; lights `L:B757_MAIN_APDISC_1_DECAL`, `L:B757_MAIN_ATDISC_1_DECAL`.
- Access keys (R57, by node): Heading `B757_MCP_AP_HDG_SEL_SW` S, `B757_MCP_HOLD_1_BUTTON` H, `B757_MCP_LNAV_BUTTON` L. Speed `B757_MCP_SPD_BUTTON` S, `B757_MCP_THR_BUTTON` T, `B757_MCP_FLCH_BUTTON` F, `B757_MCP_AP_SPD_MACH_SW` M, `B757_MCP_AP_SPD_HOLD_SW` I. Altitude `B757_MCP_HOLD_2_BUTTON` H, `B757_MCP_VNAV_BUTTON` V, `B757_MCP_FLCH_BUTTON` F, `BBS757_ALTSEL_CLICK` I (Task 5). V/S `B757_MCP_VS_BUTTON` V.
- Captions overriding the map: `B757_MCP_AP_SPD_HOLD_SW` "Speed Intervene", `BBS757_ALTSEL_CLICK` "Altitude Intervene". Tests build in-memory controls; never rely on plan 02's labels.
- `ValueInputForm` is frozen: while read-only it shows its own "Engage mode first to enter a value". The 757's reason ("the speed window is blank in VNAV") is said by Shift+S and the Speed Intervene caption; a per-window read-only reason is a handoff request to plan 16.
- `ProcessSimVarUpdate` gets no `SimConnectManager`: rows whose speech depends on another row keep that row's last delivered value in a field.
- Mach range 0.40-0.86; spoken "Mach 0.78" (two decimals, invariant).
- New announcement keys share a `Name` with existing OnRequest keys, never with another Continuous key ([VAR-7]); the OnRequest keys stay, so the sets' fresh reads are unchanged ([SIM-1]).
- Variables added: 10.
- A continuous row is delivered only when it CHANGES, about once a second (`SimConnectManager.ContinuousBatchDelivered`'s note), so a value that rests is never delivered again: the resting MCP values are released by a timer, outside MainForm's Ctrl+M wrap, and that release checks `SettingsManager.Current.BBS757DisabledMonitorVariablesSet` for the row's key itself ([VAR-8], `DefAnnounceMuteSets`' remarks). R60's 800 ms is shorter than the 1 s delivery period and would speak every step of a spin, so the rest is `Bbs757RestingValue.RestMs` = 1500 (one delivery period with no change, plus margin).

## Review Focus

- Typing a Mach on the ground: the changeover does nothing (measured), so the app refuses aloud after waiting for the flag and never steps the knob in the wrong units (Task 3 test).
- Spinning the heading in the cockpit speaks only the value that rests, with no further delivery after it (Task 4 test: 090, 095, 100 delivered 1 s apart, then nothing → one "MCP heading 100" at 1500 ms after the last).
- A muted MCP row stays silent when its value rests (Task 4 test: the timer's release asks the mute check).
- The app's own Ctrl+H set says "Heading 090" once; the background row adds nothing (Task 4 test).
- No window repeats an access key (Task 1 test over all four windows).
- The speed box opens again when Speed Intervene opens the window (Task 2 test with `BBS_IAS_BLANKED` 1 then 0 deliveries).

---

### Task 0 [controller]: Claim this plan

**Who:** whoever runs the plan, before any other task; with subagents, the controller itself, never an implementer. A claim counts only once it is on the remote `B757`: one that exists only on your machine claims nothing, and if someone else's claim reaches `B757` first, the plan is theirs.

- [ ] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/B757 -- docs/b757/plans/02-gaps/05-mcp.md`. Anything but "not started" and "none" means the plan is taken: stop.
- [ ] **Step 2: Branch and claim.** Branch as in the index's workflow step 1. In this file's "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the commit you branched from. Commit only that change: `docs(b757): claim plan 05`, with the `Plan: 05` trailer.
- [ ] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/B757`, `git push <remote> HEAD:B757`. If the rebase stops on these lines, someone claimed the plan first: `git rebase --abort` and stop. If the push is rejected, repeat this step. Task 1 starts only once the push has gone through.

### Task 1: Fixed access keys and Speed Intervene

**Files:** `Aircraft/BBS757/Bbs757McpWindowLayout.cs`, `Aircraft/BBS757Definition.Mcp.cs`; tests `Bbs757McpWindowTests.cs` (rewrite the moved key and button tests to the new table), `Bbs757McpTests.cs`.

**Interfaces:**
- `NodesFor(HotkeyAction)` gains `B757_MCP_AP_SPD_HOLD_SW` at the end of Speed; `public static char? AccessKeyFor(string node)`; `public static string? CaptionOverride(string node)`; `public static IReadOnlyList<string> Captions(IReadOnlyList<string> nodes, Func<string, string> labelOf)` ("&"-marked from the table; an `&` in a label doubled). The old `Captions(labels)` goes with its tests.
- `BBS_IAS_BLANKED` registered Continuous, not announced, `ExcludeFromMonitorManager` (one batch slot: the windows' 400 ms button refresh reads the cache). Speed Intervene's state text: "Window open" (0), "Window blank" (1), "" unread; its press is the panel's (`PressFromPanelAsync`): "Speed Intervene pressed".

- [ ] **Step 1: Write the failing tests:** `Speed_window_keys` (`&SPD`, `&THR`, `&FLCH`, `IAS/&MACH`, `Speed &Intervene`); `No_window_repeats_a_key`; `Heading_and_vs_keys_unchanged`; `Speed_intervene_state_text`.
- [ ] **Step 2: Run** `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "Bbs757McpWindowTests|Bbs757McpTests"`. Expected: FAIL.
- [ ] **Steps 3-4:** implement; full suite PASS.
- [ ] **Step 5: Commit** `feat(b757): MCP windows' fixed Alt keys and Speed Intervene`.

### Task 2: The speed box in VNAV, and Shift+S

**Files:** `BBS757Definition.Mcp.cs`, `Bbs757Mcp.cs`; test `Bbs757McpTests.cs`.

**Interfaces:**
- The Ctrl+S window's input-enabled check: enabled unless the cached `BBS_IAS_BLANKED` is 1 (R58).
- `Bbs757Mcp.SpeedReadout(double? blanked, double? machFlag, double? knots, double? mach)`: blank → "MCP speed window blank. Speed Intervene opens it."; Mach flag 1 and Mach read → "MCP Mach 0.78"; Mach flag 1 and unread → "MCP is in Mach mode. Mach is not read yet."; else "Speed 250" (today's words).
- `BBS_TARGET_MACH` registered OnRequest, `ExcludeFromMonitorManager`; Shift+S reads the four values fresh.

- [ ] **Steps 1-5** (tests `Speed_box_closed_while_blank`, `Speed_readout_cases` incl. de-DE); commit `feat(b757): speed box closed in VNAV, Shift+S reads blank and Mach`.

### Task 3 [in flight]: Mach entry

**Files:** Create `Aircraft/BBS757/Bbs757Mach.cs`; modify `BBS757Definition.Mcp.cs`, `Bbs757Mcp.cs`; test `Bbs757MachTests.cs`; `flight-05.md`.

- [ ] **Step 1 (owner, in flight, cruise or climbing through FL280):** record `BBS_AIRSPEED_IN_MACH`, `BBS_TARGET_MACH` and the drums `B757_MCP_Gau_SPD_AUTO_100/10/1`; press IAS/MACH if in IAS and record the same; with the window open (Speed Intervene if needed), one `K:AP_SPD_VAR_INC`, then bursts of 5, 10, 20 and 30 in one calculator call each, 2 s apart, recording the target after each; restore. Also (M6) press SPD in flight and record its light and the FMA (Alt+P); (M2) in a VNAV climb press Speed Intervene and record `BBS_IAS_BLANKED` and the speed target. Use `docs/b757/tools/lvar-snapshot`; record in `flight-05.md`.
- **Interfaces:**
  ```csharp
  public readonly record struct Bbs757SpeedEntry(int? Knots, double? Mach);
  public static class Bbs757Mach
  {
      public const double Min = 0.40, Max = 0.86;
      public static bool TryParse(string? text, out Bbs757SpeedEntry entry, out string error);  // "250", "0.78", ".78", "M.78", "m0.78"
      public static (int Bulk, int Rest) Bursts(int hundredths);   // step 1's tiers; (0, n) if linear
      public static string Phrase(double? mach);                   // "Mach 0.78" / "Mach unavailable"
  }
  ```
  Error (exact): "Enter a speed from 100 to 399 knots, or a Mach number from 0.40 to 0.86." The speed set (R59): units differing from the flag → press IAS/MACH silently, wait up to 2 s (reads every 250 ms); unchanged → "IAS/MACH changeover is not available now", nothing else. Mach: read the target (the variable step 1 confirms, else the drums), send `Bursts` as `SpeedBurstCode` bursts with `SpeedBurstGapMs`, correct in linear bursts of at most `LinearBurstMax`, speak `Phrase` of the read-back. The old "Mach entry is not supported yet" refusal goes.
- [ ] **Steps 2-6:** tests `Parses_knots_and_mach` (de-DE "0,78"), `Refuses_out_of_range`, `Mach_set_against_a_fake_mcp` (step 1's tiers: 0.70 → 0.78 and 0.80 → 0.74 with no correction), `Changeover_refused_on_the_ground`; run, implement, full suite; commit `feat(b757): Mach entry through the speed window`.

### Task 4: Autoflight announcements

**Files:** Create `Aircraft/BBS757/Bbs757AutoflightAnnouncements.cs`; modify `BBS757Definition.Mcp.cs` (variables; implements plan 01's `ProcessAutoflightUpdate`, `ResetAutoflight`; rebaselines after the sets); test `Bbs757AutoflightAnnouncementsTests.cs`.

**Interfaces:**
- Rows (LVar, Continuous, `IsAnnounced`, a Ctrl+M row named by the label, no panel): `BBS757_MCP_HEADING_WATCH` → `BBS_AP_TARGET_HEADING` "MCP Heading"; `BBS757_MCP_ALTITUDE_WATCH` → `BBS_AP_TARGET_ALTITUDE` "MCP Altitude"; `BBS757_MCP_VS_WATCH` → `BBS_AP_TARGET_VS` "MCP Vertical Speed"; `BBS757_MCP_SPEED_WATCH` → `BBS757_ORANGE_AIRSPEED_BUGS` "MCP Speed"; `BBS757_MCP_MACH_WATCH` → `BBS_TARGET_MACH` "MCP Mach"; `BBS757_MCP_MODE_WATCH` → `BBS_AIRSPEED_IN_MACH` (silent, `ExcludeFromMonitorManager`); `BBS757_AT_ARMED` → `BBS_AUTO_THROTTLE` "A/T Armed"; `BBS757_AP_DISC_LIGHT` → `B757_MAIN_APDISC_1_DECAL` "Autopilot Disconnect"; `BBS757_AT_DISC_LIGHT` → `B757_MAIN_ATDISC_1_DECAL` "Autothrottle Disconnect".
- Produces:
  ```csharp
  public sealed class Bbs757RestingValue
  {
      public const int RestMs = 1500;
      public void Changed(string phrase, long nowMs);   // a delivery with a new value; the first after Reset is the silent baseline
      public string? TakeDue(long nowMs);               // the phrase once it has rested RestMs and differs from the last spoken (then counts as spoken); else null
      public bool Pending { get; }                      // a change waits to rest
      public void Rebaseline(string phrase);            // the app's own set: counts as spoken
      public void Reset();
  }
  public static class Bbs757AutoflightAnnouncements
  {
      public static string HeadingPhrase(double v);   // "MCP heading 090"
      public static string AltitudePhrase(double v);  // "MCP altitude 12000"
      public static string VsPhrase(double v);        // "MCP vertical speed minus 1500" / "… 0"
      public static string SpeedPhrase(double v);     // "MCP speed 250"
      public static string MachPhrase(double v);      // "MCP Mach 0.78"
      public static string? AtArmedPhrase(double v);  // "A/T armed" / "A/T disarmed"
      public static string? DiscLightPhrase(string what, double v);   // 1 "Autopilot disconnect" / "Autothrottle disconnect", 0 null
  }
  ```
- `ProcessAutoflightUpdate`: keeps the last delivered `BBS_IAS_BLANKED` (Task 1's Continuous key) and mode watch in fields; the five MCP rows feed one `Bbs757RestingValue` each with `Changed` (R60): speed only while not blank and in IAS, Mach only in Mach mode; it keeps the announcer and starts a `System.Windows.Forms.Timer` (UI thread, 250 ms) while any row is `Pending`. Each tick: for each row, `TakeDue(now)`; a due phrase is spoken with `announcer.Announce` unless its key is in `BBS757DisabledMonitorVariablesSet` (then dropped); the timer stops when nothing is pending. The A/T and light rows go through `_changes.Observe` inside the update, where MainForm's wrap mutes them. `handled = true` for these keys. The MCP sets rebaseline the matching row with the value read back; `ResetAutoflight` resets the five and stops the timer.

- [ ] **Steps 1-5** (tests `Heading_spoken_once_it_rests_without_another_delivery`, `Spin_speaks_only_the_last_value`, `First_reading_is_silent`, `Own_set_is_not_repeated`, `Muted_row_is_dropped_when_it_rests`, `Speed_silent_while_blank`, `Mach_only_in_mach_mode`, `Disconnect_lights`, `A_T_armed_phrases`, `Phrases_invariant_under_de_DE`; the release logic is a pure method taking the clock and the mute check, so the tests need no timer; full suite with `VarNameCollisionTests`); commit `feat(b757): MCP values, A/T arm and the disconnect lights announced`.

### Task 5 [in flight]: Altitude Intervene

- Waits for plan 02 Task 8's flight check (`flight-02.md`). Works: add `BBS757_ALTSEL_CLICK` to the Altitude window (key I, caption "Altitude Intervene") and tell plan 14, in this plan's handoff, to add it to Ctrl+P (key J); test the layout. Does nothing: leave both out; the handoff tells plan 16 to say BlueBird's 757 does not model altitude intervention and a constraint is deleted on the CDU's LEGS page.
- [ ] **Steps 1-5**; commit `feat(b757): altitude intervene per the flight check`.

## In-sim checks (owner)

1. Ground: Ctrl+S, Alt+I says "Speed Intervene pressed"; typing 0.78 says "IAS/MACH changeover is not available now".
2. Climb (VNAV): the speed box is closed; Alt+I opens the window and the box takes 290; at cruise, 0.78 → "Mach 0.78"; Shift+S reads it; an MCP altitude change from the panel → "MCP altitude …" once.
3. Disconnect the autopilot (the panel's Autopilot Disengage Bar or the yoke button): "Autopilot disconnect".

## Handoff to plan 16

- **Pilot-facing:** "757 MCP windows: fixed Alt keys matching the PMDG windows (IAS/MACH moves to Alt+M), Speed Intervene (Alt+I), Mach entry (type 0.78), a speed box closed while VNAV blanks the window (Shift+S says so); MCP value changes, A/T arm and the A/P and A/T disconnect lights are announced (each in Ctrl+M). The SPD button is the autothrottle's speed mode and does nothing on the ground." (`feature`).
- **Handoff request (optional):** `Forms/ValueInputForm.cs`: an optional read-only reason text per window, so the 757's speed box can say why it is closed.
- **docs/b757.md:** MCP windows (key table, Mach, blank window), "Autoflight announcements".

## Execution record

Kept in this file only, so no other plan's push ever touches it. Task 0 claims the plan (**Status**, **Claimed by**,
**Started**); fill in the rest as you go and when the plan lands (index, "Records").

- **Status:** not started
- **Claimed by:** none
- **Worked by:** (people; add "with subagents" if run through superpowers:subagent-driven-development)
- **Started:** (date; the `B757` commit you branched from)
- **Landed:** (date; the last commit of this plan on `B757`)
- **Tests at landing:** (full suite passed / failed, after the last rebase)
- **Tasks:** (done; left, and why)
- **Waiting on the owner:** ([owner] and [in flight] tasks and in-sim checks not run yet)
- **In-sim results:** (each check: passed or failed, date; or "none run")
- **Changed from the plan:** (a name, a value or a task changed, dropped or added, and why)
- **Rulings made while working:** (question, answer, who decided)
