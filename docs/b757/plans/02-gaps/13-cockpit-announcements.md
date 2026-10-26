# BlueBird 757 Cockpit-Wide Announcements Implementation Plan (plan 13, later: new features)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** Like the PMDG 737, which speaks about 400 switch and light rows, the 757 announces every meaningful annunciator light and any panel switch moved by something other than the pilot's own pick (a preset, a quick start, BlueBird's automation, a hardware panel), each mutable in Ctrl+M; and, if a flight shows where BlueBird keeps them, the flight mode changes and localizer and glideslope capture.

**Architecture:** The SimConnect budget cannot carry hundreds more rows (the 757 stays under 900 variables), but the Coherent debugger reads all 2,173 BlueBird variables in one call in about 0.2 s (session 8). So the tablet agent (the one socket on the `BBSTablet` view, [DBG-9]) gains a watch list: it keeps the last value of each watched L:var and returns only the changes; the client polls it twice a second and raises an event. The definition turns changes into speech: lights from a curated table with phrases, switches from the map's own labels and positions, skipping anything another plan already announces, anything the pilot just picked, and summarizing a burst (a preset moves dozens of switches at once).

**Tech Stack:** C# 13 / .NET 10 Windows Forms, xUnit, ES5 agent.

**Spec:** `docs/b757/spec-gaps.md` sections 2 (the "Throughout" row), 4 (R66, R70), 7; `docs/b757/evidence/controls/lights.tsv` (518 lights), the generated control map; plan 01's `OnOwnWrite`, `OnConnectionBatch`, `ResetAnnouncements` hooks and the definition-owned tablet client.

## Global Constraints

- Owned files: `SimConnect/BBS757/CoherentBbs757TabletClient.cs`, `Resources/coherent-bbs757-tablet-agent.js` (the client from plan 08, which adds a per-key gap to `PressKey`; the agent from plan 01), `Aircraft/BBS757Definition.Announcements.cs` (new), `Aircraft/BBS757/Bbs757CockpitAnnouncements.cs` (new), `Aircraft/BBS757/Bbs757LightTable.cs` (new); tests `CoherentBbs757TabletClientTests.cs`, `Bbs757CockpitAnnouncementsTests.cs` (new), `Bbs757LightTableTests.cs` (new), fixture `Fixtures/bbs757-lights.json` (new); `docs/b757/evidence/flight-13.md`.
- The CDU scraping, key presses and `Bbs757CduPages` behaviour of the client stay exactly as they are (plans 04, 06, 08, 09 rely on them); the watch runs on the same socket and send lock, never a second socket.
- One owner per spoken row: a watched L:var whose name any definition row registers with `IsAnnounced` (plans 05, 06, 07 and the base rows) is never watched here; this is computed from the definition's variables at start, not hard-coded.
- The pilot's own write is not said: a change to a control within 5 s of `OnOwnWrite(node)` for it, or while `IsPickPending(node)`, is that write's echo.
- Mutes: each watched row has a key `BBS757W_<L:var>` in the 757's Ctrl+M set (`UserSettings.BBS757DisabledMonitorVariablesSet`, the existing set; these announcements are spoken outside `ProcessSimVarUpdate`, so this plan checks the set itself, as the gear legs check their row, [VAR-8]).
- Ctrl+M stays the one shared dialog ([MON-1]: a title, rows and the list, no per-form UI): this plan adds rows through plan 01's `AddCockpitMonitorRows` and never touches `BBS757MonitorManagerForm`.
- Variables added to SimConnect: 0.

## Review Focus

- A preset that moves 40 switches in one second speaks one summary, not 40 lines (Task 4 test).
- The pilot's own panel pick is never repeated by the watcher (Task 4 test with `OnOwnWrite` and a delivered change 1 s later).
- A light another plan announces (the EXT PWR rows, the disconnect lights, the autoland status) is not watched (Task 3 test against the definition's variables).
- The watch list survives an aircraft reload (the agent reinstalled): the client re-sends it and the first change set after a reinstall is a silent baseline (Task 2 test).
- A muted row stays silent and the mute persists (Task 5 test).

---

### Task 0 [controller]: Claim this plan

**Who:** whoever runs the plan, before any other task; with subagents, the controller itself, never an implementer. A claim counts only once it is on the remote `B757`: one that exists only on your machine claims nothing, and if someone else's claim reaches `B757` first, the plan is theirs.

- [ ] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/B757 -- docs/b757/plans/02-gaps/13-cockpit-announcements.md`. Anything but "not started" and "none" means the plan is taken: stop.
- [ ] **Step 2: Branch and claim.** Branch as in the index's workflow step 1. In this file's "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the commit you branched from. Commit only that change: `docs(b757): claim plan 13`, with the `Plan: 13` trailer.
- [ ] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/B757`, `git push <remote> HEAD:B757`. If the rebase stops on these lines, someone claimed the plan first: `git rebase --abort` and stop. If the push is rejected, repeat this step. Task 1 starts only once the push has gone through.

### Task 1 [in flight]: Look for flight modes and glideslope

**Files:** `docs/b757/evidence/flight-13.md`.

- [ ] On the owner's flight, with the snapshot tool (`docs/b757/tools/lvar-snapshot`), snapshot before and after each: THR REF at takeoff, VNAV SPD/PTH engaging, ALT capture at cruise, LOC armed and captured, G/S armed and captured, FLARE and ROLLOUT armed, an A/P disconnect, an A/T disconnect. Record Alt+P's FMA reading at each and the diff (minus the noise list). Also record the stock `NAV HAS GLIDE SLOPE:1`, `NAV GSI:1`, `NAV CDI:1` at G/S capture. Write which variables change with each mode, if any, in `flight-13.md`. Task 6 builds on it.

### Task 2: The agent's watch list and the client's event

**Files:** `Resources/coherent-bbs757-tablet-agent.js`, `SimConnect/BBS757/CoherentBbs757TabletClient.cs`; test `CoherentBbs757TabletClientTests.cs`.

**Interfaces:**
- Agent: `A.watch(namesJson)` stores the names (an array of L:var names, without `L:`) and resets the baseline; `A.changes()` reads each name with `SimVar.GetSimVarValue('L:'+n,'number')` and returns JSON `{"seq":n,"changes":{name:value,…}}` with only the names whose value differs from the last call (the first call after `watch` returns an empty map: the baseline). The agent version goes up by one.
- Client: `public void SetWatchList(IReadOnlyCollection<string> names)`; `public event Action<IReadOnlyDictionary<string, double>>? LvarsChanged` (posted to the UI context); poll `changes()` every 500 ms while the list is non-empty, through the existing socket and send lock; after a reinstall, send the list again (its first answer is the silent baseline); `internal static string WatchExpression(IReadOnlyCollection<string> names)` for tests.

- [ ] **Step 1: Write the failing tests:** `Watch_expression_is_json` (names with no quotes break nothing); `Agent_has_watch_and_changes` (source scan); `Reinstall_resends_the_list` (the client's reinstall path calls `watch` again, via a test seam on the eval function).
- [ ] **Steps 2-5:** run (FAIL), implement, full suite (the CDU client tests unchanged), commit `feat(b757): the tablet agent watches L:vars and reports changes`.

### Task 3: The light table and the watch set

**Files:** Create `Aircraft/BBS757/Bbs757LightTable.cs`, `tests/MSFSBlindAssist.Tests/Fixtures/bbs757-lights.json` (the L:var names of `docs/b757/evidence/controls/lights.tsv` as a JSON array, made once by this task; tests never read `docs/b757/`, which plan 16 deletes); test `Bbs757LightTableTests.cs`.

**Interfaces:**
  ```csharp
  public sealed record Bbs757Light(string LVar, string Label, string OnPhrase, string? OffPhrase);   // OffPhrase null: silent going out
  public static class Bbs757LightTable
  {
      public static IReadOnlyList<Bbs757Light> All { get; }
      public static IReadOnlyList<string> WatchSet(IEnumerable<Bbs757Light> lights, IEnumerable<Bbs757Control> controls, ISet<string> announcedElsewhere);
  }
  ```
- `All`: curated from `docs/b757/evidence/controls/lights.tsv`: the caution and fault annunciators a pilot acts on (names containing FAULT, OFF, INOP, LOW, DISCH, FAIL, PRESS, OVHT, ON DC, VALVE where it is a fault light, and the gear, door and fire warnings not already announced elsewhere), with plain phrases ("L bus off light on", "APU fault light on", "Left pack off light on"; OffPhrase "… off" for faults, null for indications). Leave out backlights, legends that only follow a switch (the switch's own announcement says it), test lights and the 511 breakers. Keep the list in one readable table with a comment per group.
- `WatchSet`: the lights' L:vars plus every map control's state var (not momentary buttons), minus `announcedElsewhere` (the `Name`s of the definition's `IsAnnounced` variables).

- [ ] **Steps 1-5** (tests `Table_has_no_duplicates_and_every_lvar_is_in_the_lights_fixture`, `Watch_set_skips_names_announced_elsewhere`, `Momentary_buttons_are_not_watched`, `No_backlight_or_test_light`); commit `feat(b757): the cockpit light table`.

### Task 4: Changes into speech

**Files:** Create `Aircraft/BBS757/Bbs757CockpitAnnouncements.cs`, `Aircraft/BBS757Definition.Announcements.cs` (implements plan 01's `OnOwnWrite`, `OnConnectionBatch`, `ResetAnnouncements`); test `Bbs757CockpitAnnouncementsTests.cs`.

**Interfaces:**
  ```csharp
  public sealed class Bbs757CockpitAnnouncements
  {
      public Bbs757CockpitAnnouncements(IReadOnlyList<Bbs757Light> lights, IReadOnlyList<Bbs757Control> controls, Func<long> nowMs);
      public void OwnWrite(string node);                                   // stamps the echo window (5 s)
      public IReadOnlyList<string> Changes(IReadOnlyDictionary<string, double> changes, Func<string, bool> isPickPending, Func<string, bool> isMuted);
      public void Reset();
  }
  ```
  `Changes` returns what to say: a light → its phrase; a switch → "{label} {position name}" from the map ("Seatbelts AUTO", "L Pack Selector OFF"); skipped when muted, inside the own-write window or pending; a first sighting of a name after `Reset` is a baseline. A burst (more than 5 switch changes in one call or within 1 s) → one summary "Panel state changed: 37 switches" plus every light phrase (lights are never summarized away).
- `OnConnectionBatch`: build the watch set from the definition's variables, `EnsureTabletClient().SetWatchList`, subscribe `LvarsChanged` once; speak each returned line as a background announcement (CORE-7). `OnOwnWrite` → `OwnWrite`; `ResetAnnouncements` → `Reset`.

- [ ] **Steps 1-5** (tests `Light_phrase`, `Switch_phrase_from_map_positions`, `Own_write_not_repeated`, `Pending_pick_not_repeated`, `Burst_is_summarized_but_lights_still_speak`, `First_sighting_is_silent`, `Muted_row_silent`); commit `feat(b757): annunciator lights and switch moves announced from any source`.

### Task 5: Ctrl+M for hundreds of rows

**Files:** `Aircraft/BBS757/Bbs757CockpitAnnouncements.cs` (the rows and the mute test), `Aircraft/BBS757Definition.Announcements.cs` (implements plan 01's `AddCockpitMonitorRows`); test `Bbs757CockpitAnnouncementsTests.cs`.

**Interfaces:**
- `public static IReadOnlyList<MonitorRow> MonitorRows(IReadOnlyList<Bbs757Light> lights, IReadOnlyList<Bbs757Control> watchedSwitches)`, in this order: "All Annunciator Lights" (`BBS757W_ALL_LIGHTS`), "All Switches From Any Source" (`BBS757W_ALL_SWITCHES`, the burst summary included), one "Switches: {panel}" row per panel (`BBS757W_PANEL_{panel}`), then one "Light: {label}" row per light and one "Switch: {panel}, {label}" row per switch (`BBS757W_{L:var}`). The shared dialog's search finds "Light:", "Switch:" or a panel's name, so hundreds of rows stay workable (the PMDG list has about 400).
- `public static bool IsMuted(string lvar, bool isLight, string? panel, Func<string, bool> inMuteSet)`: muted when the row's own key, its panel's key or its group's key is in the set. `Changes` (Task 4) asks it for every line.

- [ ] **Steps 1-5** (tests `Rows_have_unique_keys_and_prefixed_labels`, `Group_rows_come_first`, `Panel_row_mutes_its_switches`, `All_lights_row_mutes_every_light_but_no_switch`, `All_switches_row_mutes_the_burst_summary`, `Rows_appended_after_todays_rows` through the definition's `MonitorRows()`); commit `feat(b757): Ctrl+M rows for the cockpit-wide announcements`.

### Task 6 (only if Task 1 found sources): Flight modes and capture

- If Task 1 found variables that follow the FMA modes or localizer and glideslope capture, add them to the light table with phrases ("LOC captured", "G/S captured", "VNAV PTH", "FLARE armed", …), each a Ctrl+M row, and in the handoff ask plan 16 to propose bringing back a 757 "glideslope alive" from that source. If nothing was found, the handoff says FMA and capture stay with Alt+P.
- [ ] **Steps 1-5**; commit `feat(b757): flight mode and capture announcements`.

## In-sim checks (owner)

1. Load the Before start preset from Shift+T: one "Panel state changed: … switches" and the lights that came on or went out; toggle a switch with a hardware panel or the mouse: "{label} {position}"; pick the same switch in the app: no repeat.
2. Ctrl+M: mute "All Annunciator Lights", fly a while, unmute; mute one panel's switches and move one of them with the mouse: nothing said.

## Handoff to plan 16

- **Pilot-facing:** "757: every meaningful annunciator light, and any switch moved by something other than your own pick in the app (a preset, a quick start, BlueBird's automation, a hardware panel), is announced; a preset's many switches are summed up in one line; each light and switch can be muted in Ctrl+M (rows starting "Light:" and "Switch:", plus one row each to mute all lights, all switches or one panel's switches)." Changelog `feature`.
- **Proposed rule line:** `- [B757-11] Cockpit-wide 757 announcements watch L:vars through the tablet agent's watch list (one Coherent call, no SimConnect slot), never as SimConnect rows, and skip any L:var a definition row already announces: `Bbs757LightTable.WatchSet` computes that from the registered `IsAnnounced` names. Full: docs/invariants/b757.md#b757-11`
- **docs/b757.md:** "Cockpit-wide announcements" (the watcher, the table, the burst summary, the own-write window, the Ctrl+M rows and their group mutes).

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
