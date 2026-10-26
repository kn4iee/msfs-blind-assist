# BlueBird 757 Gaps Foundation Implementation Plan (plan 01, first: foundation)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Lay everything plans 02-15 need so the early plans (02-08) can work in parallel and the later ones (09-15) can follow, without two plans touching one file at a time: partial-method hooks on `BBS757Definition`, test files split by owner, the definition owning the one tablet client, a service that reads and types on the first officer's CDU, the shared variable keys, the map loader's guard fields, the L:var snapshot tool, and one fix: no glideslope call-out from the simulator's NAV 1 on the 757.

**Architecture:** `BBS757Definition` already composes itself from `Add…Variables` calls, one panel builder, one display-override method, one hotkey switch and one `ProcessSimVarUpdate`. This plan adds calls at each of those points to optional `partial void` methods declared in a new `BBS757Definition.Hooks.cs`; a plan implements its group in its own partial file and an unimplemented hook compiles away. The tablet client (the one Coherent socket on the `BBSTablet` view, [DBG-9]) moves from MainForm to the definition, so background features can use it from the moment the 757 loads. `Bbs757CduPages` wraps it with read-a-page / wipe / type / line-key operations.

**Tech Stack:** C# 13 / .NET 10 Windows Forms, xUnit, ES5 in-page agent, PowerShell 7 and Python 3 for the snapshot tool.

**Spec:** `docs/b757/spec-gaps.md` (sections 3.2, 3.9, 4 R52 and R68-R69, 5); index and ownership: `docs/b757/plans/02-gaps/00-index.md`.

## Global Constraints

- Seams change no behaviour: every existing test passes (the three loosened and the moved ones included), and an unimplemented hook leaves the old path as it was. The one intended change a pilot hears: no "glideslope alive / lost" on the 757.
- Hook signatures are the contract in Task 1's Interfaces block; later plans implement them verbatim. After this plan: `BBS757Definition.cs`, `.Hooks.cs`, `.Altimeter.cs`, `BBS757MonitorManagerForm.cs`, `Bbs757SharedKeys.cs`, `Bbs757CduPages.cs`, `docs/b757/tools/**` and `Bbs757DefinitionTests.cs` are frozen; `.Interaction.cs` passes to plan 03, `.Readouts.cs` and `Bbs757FlightReadinessTests.cs` to 06 (then 14), the tablet client and agent to 13, `Bbs757ControlMap.cs` to 02 (then 12), `MainForm.BBS757.cs` to 07 (then 11).
- Background CDU work uses CDU 2 (the first officer's) only (R52).
- CLR is held 1300 ms only by `Bbs757CduPages.WipeScratchpadAsync`; every other key keeps the 250 ms hold (`CoherentBbs757TabletClient.KeyHoldMs`). Holds are clamped to 100-1500 ms in the client and the agent.
- US English; CORE-7 speech; CORE-16 read before edit.

## Review Focus

- The pilot is typing on CDU 2 in the CDU window while the app wants it: `Bbs757CduPages` refuses (returns null) rather than interleave keys (`PendingKeyCount > 0`), Task 5 test.
- A CDU message ("INVALID ENTRY") covers the scratchpad after typing: `EnterAsync` sees the scratchpad does not hold the typed text and presses no line key, Task 5 test.
- The tablet view is unreachable (aircraft reloading, debugger port held): every `Bbs757CduPages` call returns null within its timeout and never throws, Task 5 test.
- Switching away from the 757 with the CDU window open: the window closes before the definition disposes the client, and nothing throws (Task 3 test of the order; in-sim check 1).
- The glideslope gate leaves every other aircraft's glideslope row and call-out as before, Task 4 test.

---

### Task 1: Partial-method seams and shared keys

**Files:**
- Create: `MSFSBlindAssist/Aircraft/BBS757Definition.Hooks.cs`, `MSFSBlindAssist/Aircraft/BBS757/Bbs757SharedKeys.cs`
- Modify: `MSFSBlindAssist/Aircraft/BBS757Definition.cs` (`BuildVariables`, `BuildPanelsOnce`), `BBS757Definition.Interaction.cs` (`HandleUIVariableSet`, `ProcessSimVarUpdate`, `OnSimContextReset`, `OnSwitchedAway`, `HandleHotkeyAction`, `PressAsync`, `SetPositionAsync`), `BBS757Definition.Altimeter.cs` (`HandleUITypedText`, the altimeter set and `SetSquawkAsync`), `BBS757Definition.Readouts.cs` (`TryGetDisplayOverride`), `Forms/BBS757/BBS757MonitorManagerForm.cs` (takes rows), `MainForm.BBS757.cs` (`ShowBBS757MonitorManagerDialog`)
- Test: `tests/MSFSBlindAssist.Tests/Bbs757SeamsTests.cs`

**Interfaces:**
- Produces, in `BBS757Definition.Hooks.cs` (all `partial void`, no access modifier, so each is optional; `D` = `Dictionary<string, List<string>>`):
  ```csharp
  // Plan 03 (panel writes), then plan 12
  static partial void AddOverheadVariables(Dictionary<string, SimVarDefinition> vars);
  partial void AddOverheadPanels(D structure, D controls, D displays);
  partial void HandleOverheadTypedText(string varKey, string text, SimConnectManager simConnect, ScreenReaderAnnouncer announcer, ref bool handled);
  partial void HandleOverheadSet(string varKey, double value, SimConnectManager simConnect, ScreenReaderAnnouncer announcer, ref bool handled);
  partial void FormatOverheadDisplay(string varKey, double value, ref string? text);
  // Plan 04 (radios)
  static partial void AddRadioVariables(Dictionary<string, SimVarDefinition> vars);
  partial void AddRadioPanels(D structure, D controls, D displays);
  partial void HandleRadioSet(string varKey, double value, SimConnectManager simConnect, ScreenReaderAnnouncer announcer, ref bool handled);
  partial void HandleRadioTypedText(string varKey, string text, SimConnectManager simConnect, ScreenReaderAnnouncer announcer, ref bool handled);
  partial void ProcessRadioUpdate(string varName, double value, ScreenReaderAnnouncer announcer, ref bool handled);
  partial void HandleRadioHotkey(HotkeyAction action, SimConnectManager simConnect, ScreenReaderAnnouncer announcer, Form parentForm, HotkeyManager hotkeyManager, ref bool handled);
  partial void FormatRadioDisplay(string varKey, double value, ref string? text);
  partial void ResetRadios();
  partial void CloseRadioWindows();
  // Plan 05 (MCP and autoflight announcements)
  partial void ProcessAutoflightUpdate(string varName, double value, ScreenReaderAnnouncer announcer, ref bool handled);
  partial void ResetAutoflight();
  // Plan 07 (EFB ground power), then plan 11
  static partial void AddEfbVariables(Dictionary<string, SimVarDefinition> vars);
  partial void AddEfbPanels(D structure, D controls, D displays);
  partial void ProcessEfbUpdate(string varName, double value, ScreenReaderAnnouncer announcer, ref bool handled);
  partial void FormatEfbDisplay(string varKey, double value, ref string? text);
  // Plan 06 (readouts), then plan 14
  partial void HandleReadoutHotkey(HotkeyAction action, SimConnectManager simConnect, ScreenReaderAnnouncer announcer, Form parentForm, HotkeyManager hotkeyManager, ref bool handled);
  partial void OnOwnSquawkSet(string code);
  partial void OnOwnAltimeterSet();
  // Plan 15 (thrust levers)
  static partial void AddThrustVariables(Dictionary<string, SimVarDefinition> vars);
  partial void AddThrustPanels(D structure, D controls, D displays);
  partial void HandleThrustSet(string varKey, double value, SimConnectManager simConnect, ScreenReaderAnnouncer announcer, ref bool handled);
  partial void HandleThrustTypedText(string varKey, string text, SimConnectManager simConnect, ScreenReaderAnnouncer announcer, ref bool handled);
  partial void FormatThrustDisplay(string varKey, double value, ref string? text);
  partial void ResetThrust();
  // Plan 10 (First Officer)
  static partial void AddFirstOfficerVariables(Dictionary<string, SimVarDefinition> vars);
  // Plan 13 (cockpit-wide announcements)
  partial void OnOwnWrite(string node);
  partial void OnConnectionBatch(ScreenReaderAnnouncer announcer);
  partial void ResetAnnouncements();
  // Ctrl+M rows for speech that is not a variable's (plan 09: CDU messages; plan 13: lights and switches)
  partial void AddCduMonitorRows(List<MonitorRow> rows);
  partial void AddCockpitMonitorRows(List<MonitorRow> rows);
  ```
- Call order (the contract):
  - `BuildVariables`, after `AddReadoutVariables`: `AddOverheadVariables`, `AddRadioVariables`, `AddEfbVariables`, `AddThrustVariables`, `AddFirstOfficerVariables`.
  - `BuildPanelsOnce`, after `AddReadoutPanels`: `AddOverheadPanels`, `AddRadioPanels`, `AddEfbPanels`, `AddThrustPanels`.
  - `TryGetDisplayOverride`, first: `FormatOverheadDisplay`, `FormatRadioDisplay`, `FormatEfbDisplay`, `FormatThrustDisplay` (stop at the first that sets `text`; return true with it).
  - `HandleUIVariableSet`, before the squawk check: `HandleRadioSet`, `HandleOverheadSet`, then `HandleThrustSet`; return true when `handled`.
  - `HandleUITypedText`, before the squawk branch: `HandleOverheadTypedText`, `HandleRadioTypedText`, `HandleThrustTypedText`; return true when `handled`.
  - `ProcessSimVarUpdate`, after `IsPickPending`: `ProcessRadioUpdate`, `ProcessAutoflightUpdate`, `ProcessEfbUpdate` (stop at the first that sets `handled`), then the existing `ProcessReadoutUpdate`.
  - `OnConnectionBatch(announcer)`: once per connection and per flight load, from the delivery that settles the BBS Nest check (`WarnOnceIfNestMissing`, whichever path settles it). `DeferredFlushWatchVariable` is unchanged.
  - `HandleHotkeyAction`, after `Sim = simConnect;`: `HandleRadioHotkey`, `HandleReadoutHotkey`; return true when `handled`, before the existing switch.
  - `OnSimContextReset`, after `ResetReadoutBaselines()`: `ResetRadios(); ResetAutoflight(); ResetThrust(); ResetAnnouncements();`.
  - `OnSwitchedAway`, after `CloseAutoflightWindows()`: `CloseRadioWindows(); ResetAutoflight();` (plan 05's resting-value timer stops with the aircraft).
  - New `public IReadOnlyList<MonitorRow> MonitorRows()` in `BBS757Definition.cs`: `MonitorRowBuilder.Build(GetVariables())` (today's rows, unchanged), then `AddCduMonitorRows(rows)`, `AddCockpitMonitorRows(rows)`. `BBS757MonitorManagerForm` takes `IReadOnlyList<MonitorRow>` instead of the variables (still only a title, rows and its list, [MON-1]); `ShowBBS757MonitorManagerDialog` in `MainForm.BBS757.cs` passes `bbs757.MonitorRows()`.
  - `PressAsync` and `SetPositionAsync`: `OnOwnWrite(control.Node)` just before the first `io.Write`.
  - The altimeter set: `OnOwnAltimeterSet()` before its write; `SetSquawkAsync`: `OnOwnSquawkSet(code)` before its write.
- Produces `public static class Bbs757SharedKeys` with `public const string ExtPwrOn = "BBS757_EXT_PWR_ON", ExtPwrAvail = "BBS757_EXT_PWR_AVAIL", GpuConnected = "BBS757_GPU_CONNECTED";` (registered by plans 06, 06 and 07).

- [x] **Step 1: Write the failing test** `Bbs757SeamsTests.Hooks_file_declares_every_seam`: read `MSFSBlindAssist/Aircraft/BBS757Definition.Hooks.cs` from the repo root (as other source-scan tests find files) and assert each of the 36 method names above appears with `partial void`; `Monitor_rows_without_hooks_are_todays` (`MonitorRows()` equals `MonitorRowBuilder.Build(GetVariables())`); `Shared_keys_are_distinct` (three constants, distinct, prefix `BBS757_`); `Display_override_unchanged_without_hooks` (the 757's `TryGetDisplayOverride` gives today's text for the squawk row and the flap row).
- [x] **Step 2: Run** `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter Bbs757SeamsTests`. Expected: FAIL.
- [x] **Step 3: Implement** the hooks file (a class comment naming each group's plan), the shared keys and the calls; each call site gets a one-line comment naming the plan whose file implements it.
- [x] **Step 4: Run the full suite.** Expected: PASS.
- [x] **Step 5: Commit** `feat(b757): seams for the gap plans`.

### Task 2: Split the test files by owner

**Files:**
- Modify: `tests/MSFSBlindAssist.Tests/Bbs757DefinitionTests.cs`, `tests/MSFSBlindAssist.Tests/Bbs757FlightReadinessTests.cs`
- Create: `tests/MSFSBlindAssist.Tests/Bbs757McpWindowTests.cs`

**Interfaces:**
- `Bbs757DefinitionTests` (frozen after this): `Sections_and_panels_follow_the_map_order` checks that the map's panels appear in each section, and the map's controls in each panel, **in map order as a subsequence** (app panels and fields may sit between); `Every_status_row_is_on_the_status_display_then_the_flight_controls` checks the map's status rows then the flight-control rows appear in that order (other rows may follow); `The_pedestal_starts_with_the_control_stand_the_engines_and_the_transponder` checks the order of those three among the Pedestal's map panels.
- Move verbatim, keeping each test's name, from `Bbs757FlightReadinessTests.cs` to the new `Bbs757McpWindowTests.cs` (owned by plan 05, then 14): the sections "The autopilot window's table", "Ctrl+P's A/T Arm: BlueBird's armed flag" and "The MCP value windows' buttons", and the three window-press tests `A_window_press_the_aircraft_refused_says_so_once`, `A_window_press_that_took_is_silent`, `A_window_press_that_cannot_land_is_refused_aloud_and_sends_nothing`, with the fixture helpers they need (copied, not shared).

- [x] **Step 1: Make the edits** (no production change).
- [x] **Step 2: Run the full suite.** Expected: PASS, with the same test count as before.
- [x] **Step 3: Prove the loosening:** a throwaway local edit that inserts a dummy app panel after "TCAS and transponder" in `BuildPanelsOnce` keeps the three tests green; revert it.
- [x] **Step 4: Commit** `test(b757): split the 757 tests by plan owner`.

### Task 3: The definition owns the tablet client

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/BBS757Definition.cs`, `MSFSBlindAssist/MainForm.BBS757.cs`, `MSFSBlindAssist/Aircraft/BBS757Definition.Interaction.cs` (`OnSwitchedAway`)
- Test: `tests/MSFSBlindAssist.Tests/Bbs757SeamsTests.cs`

**Interfaces:**
- Produces on the definition: `internal CoherentBbs757TabletClient EnsureTabletClient()` (created and started on first use, the client MainForm used to own), `internal Func<CoherentBbs757TabletClient>? TabletClientFactory { get; init; }` (tests replace it), and disposal at the end of `OnSwitchedAway` (after the windows close).
- `MainForm.EnsureBbs757TabletClient()` returns `(currentAircraft as BBS757Definition)?.EnsureTabletClient()` and throws nothing when the 757 is not loaded (it returns null; its callers already only run on the 757: make them null-safe); `DisposeBBS757Forms` stops disposing the tablet client.

- [x] **Step 1: Write the failing tests:** `Tablet_client_is_created_once` (two `EnsureTabletClient` calls, one factory call); `Switching_away_disposes_it_after_the_windows` (a fake client records Dispose after the definition's windows close).
- [x] **Step 2: Run.** Expected: FAIL.
- [x] **Step 3: Implement.**
- [x] **Step 4: Run the full suite** (`CoherentBbs757TabletClientTests`). Expected: PASS.
- [x] **Step 5: Commit** `refactor(b757): the 757 definition owns the tablet client`.

### Task 4: No glideslope call-out from the simulator's NAV 1 (a fix)

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/BaseAircraftDefinition.cs` (the variable named `NAV HAS GLIDE SLOPE:1`, about line 175, and the alive/lost speech, about lines 775-786), `MSFSBlindAssist/Aircraft/BBS757Definition.cs` (one override)
- Test: `tests/MSFSBlindAssist.Tests/Bbs757GlideslopeGateTests.cs`

**Interfaces:**
- `public virtual bool UsesStockNavReceivers => true;` on `BaseAircraftDefinition` (doc comment: false for an aircraft whose own receivers leave the sim's NAV radios untouched); `BBS757Definition` overrides it `false`. When false, `GetBaseVariables` does not register the glideslope row (no Ctrl+M row, [VAR-9]) and the alive/lost branch never speaks.

- [x] **Step 1: Write the failing tests:** `Bbs757_registers_no_glideslope_row`; `Pmdg737_keeps_its_glideslope_row` (old key, `IsAnnounced` unchanged); `Bbs757_never_speaks_glideslope_alive` (the base key fed 1 then 0: nothing).
- [x] **Step 2: Run** with `--filter Bbs757GlideslopeGateTests`. Expected: FAIL.
- [x] **Step 3: Implement** (comment: spec 3.2).
- [x] **Step 4: Run the full suite** (`DefAnnounceMuteSetsTests`, `ClaudeContextBudgetTests`). Expected: PASS.
- [x] **Step 5: Commit** `fix(b757): no glideslope call-out from the sim's NAV 1 on the 757`.

### Task 5: Tablet client additions and `Bbs757CduPages`

**Files:**
- Modify: `MSFSBlindAssist/SimConnect/BBS757/CoherentBbs757TabletClient.cs`, `MSFSBlindAssist/Resources/coherent-bbs757-tablet-agent.js`, `MSFSBlindAssist/Aircraft/BBS757Definition.cs` (`CduPages`)
- Create: `MSFSBlindAssist/SimConnect/BBS757/Bbs757CduPages.cs`, `MSFSBlindAssist/SimConnect/BBS757/Bbs757CduTitle.cs`
- Test: `tests/MSFSBlindAssist.Tests/CoherentBbs757TabletClientTests.cs`, `tests/MSFSBlindAssist.Tests/Bbs757CduPagesTests.cs`

**Interfaces:**
- Client:
  - `public Task<Bbs757CduScreen?> ScrapeAsync(int cdu, CancellationToken ct)`: one `scrape(cdu, "")` through the same socket and send lock as the polling loop, independent of the window's active CDU; null when the view, agent or frame is missing or `ct` fires; never throws; leaves the polling loop's tags alone.
  - `public void PressKey(int cdu, string key, int holdMs)` (the two-argument overload calls it with `KeyHoldMs`); a queued key's cycle is its hold plus `KeyGapMs`.
  - `internal static string PressExpression(int cdu, string key, int holdMs)`, e.g. `__MSFSBA_BBS757.press(2,"KEYCLR",1300)`.
  - `internal const int AgentVersion` and `internal static bool AgentIsCurrent(int? version) => version >= AgentVersion;` the client reinstalls an agent whose `version` is missing or older (the EFB client's inline check, as a helper).
- Agent: `A.version` set to the new `AgentVersion`; `A.press(cdu, key, holdMs)` with a missing or non-numeric hold as `HOLD_MS`, clamped 100-1500.
- `Bbs757CduPages`:
  ```csharp
  public interface IBbs757CduLink
  {
      bool IsReadable { get; }
      int PendingKeyCount { get; }
      void PressKey(int cdu, string key, int holdMs);
      Task<Bbs757CduScreen?> ScrapeAsync(int cdu, CancellationToken ct);
  }
  public sealed class Bbs757CduPages
  {
      public const int FirstOfficer = 2;
      public Bbs757CduPages(IBbs757CduLink link, Func<int, CancellationToken, Task>? delay = null);
      public static Bbs757CduPages For(CoherentBbs757TabletClient client);
      public Task<Bbs757CduScreen?> ReadAsync(int cdu, CancellationToken ct);
      public Task<Bbs757CduScreen?> ShowPageAsync(int cdu, string pageKey, Func<Bbs757CduScreen, bool> isWanted, CancellationToken ct);
      public Task<bool> WipeScratchpadAsync(int cdu, CancellationToken ct);
      public Task<Bbs757CduScreen?> EnterAsync(int cdu, string text, string lineKey, CancellationToken ct);
      public Task<Bbs757CduScreen?> PressKeysAsync(int cdu, IReadOnlyList<string> keys, CancellationToken ct);
      public static string Scratchpad(Bbs757CduScreen screen);   // line 13, trimmed
      public static string Title(Bbs757CduScreen screen) => Bbs757CduTitle.PageName(screen.Lines[0]);
      public static (int Page, int Pages)? PageCount(Bbs757CduScreen screen) => Bbs757CduTitle.Counter(screen.Lines[0]);
  }
  public static class Bbs757CduTitle   // SimConnect/BBS757/Bbs757CduTitle.cs, pure; plan 09's window uses it too
  {
      public static string PageName(string titleRow);            // trimmed, trailing "n/m" removed: "ACT RTE 1 LEGS"
      public static (int Page, int Pages)? Counter(string titleRow);   // (1, 7); null when none
      public static string Identity(string titleRow);            // PageName with a leading "ACT " / "MOD " folded away: "RTE 1 LEGS"
      public static string Spoken(string titleRow);              // runs of spaces collapsed: "ACT RTE 1 LEGS 1/7"
  }
  ```
  and on the definition `internal Bbs757CduPages CduPages` (lazy, over `EnsureTabletClient()`).
- Behaviour (one `SemaphoreSlim(1, 1)`; a caller waits at most 5 s for it, then gets null):
  - `ShowPageAsync`: read; `isWanted` → return it. Else, unless `PendingKeyCount > 0` (null), press `pageKey` and read every 250 ms for up to 3 s until `isWanted`; null otherwise. (A caller wanting page 1 of a multi-page set passes a predicate for any page of it, then uses `PageCount` and `PressKeysAsync(["PREV"])`.)
  - `WipeScratchpadAsync`: `PressKey(cdu, "KEYCLR", 1300)`, wait 1800 ms, read; true when the scratchpad is blank; else one 250 ms CLR, wait 900 ms, read again.
  - `EnterAsync`: null if `!IsReadable` or `PendingKeyCount > 0`; wipe (false → null); press each key of `Bbs757CduKeys.ForChar(c)`; wait until the queue drains (at most `KeyCycleMs` × keys + 2000 ms); read; scratchpad ≠ `text` → wipe, null; press `lineKey`; wait 1200 ms; return a fresh read.
  - `PressKeysAsync`: null if `!IsReadable` or `PendingKeyCount > 0`; press each key (250 ms hold), wait for the drain, wait 1200 ms, return a fresh read.
  - Exceptions from the link are caught, logged under `BBS757`, answered with null / false.

- [x] **Step 1: Write the failing tests.** Client: `PressExpression_carries_the_hold` (exact string; 5000 → 1500; 10 → 100); `Agent_press_takes_a_hold_argument` (source scan of the agent); `Agent_version_check` (`AgentIsCurrent(AgentVersion - 1)` false, `(AgentVersion)` true, `(null)` false). Pages, against a fake link with scripted screens: `ShowPage_presses_nothing_when_already_there`; `ShowPage_presses_the_page_key_and_waits` (LEGS then NAV RADIO: one `NAVRAD`); `Enter_types_wipes_and_presses_the_line_key` (`EnterAsync(2, "113.90", "L1")`: `KEYCLR`/1300, `NUM1 NUM1 NUM3 NUMPER NUM9 NUM0`, `L1`, in order); `Enter_presses_no_line_key_when_a_message_covers_the_typing` (`INVALID ENTRY`: no `L1`, a second wipe, null); `Enter_refuses_while_the_pilot_types` (`PendingKeyCount` 3); `PressKeys_sends_the_keys_in_order` (`KEYDEL`, `L1`); `Calls_never_throw`; `Title_and_page_count` (`"  ACT RTE 1 LEGS     1/7"` → `"ACT RTE 1 LEGS"`, (1, 7), identity `"RTE 1 LEGS"`, spoken `"ACT RTE 1 LEGS 1/7"`; `"  MOD RTE 1 LEGS     1/7"` has the same identity; `"        NAV RADIO       "` → no count).
- [x] **Step 2: Run** both filters. Expected: FAIL.
- [x] **Step 3: Implement.**
- [x] **Step 4: Run the full suite** (`Bbs757CduKeysTests` reads the agent's key list: unchanged). Expected: PASS.
- [x] **Step 5: Commit** `feat(b757): the first officer's CDU as a page service`.

### Task 6: Map loader guard fields

**Files:** Modify `MSFSBlindAssist/Aircraft/BBS757/Bbs757ControlMap.cs`; test `Bbs757SeamsTests.cs`.

**Interfaces:** `Bbs757Control` gains `public string? Guard { get; set; }` (the guard control's node), `public double? GuardClosed { get; set; }`, `public double? CloseSets { get; set; }` (the value BlueBird writes to this control when the guard closes, or null), read from JSON `guard`, `guard_closed`, `close_sets` in the loader's explicit snake_case mapping (ruling R1); absent → null.

- [x] **Step 1: Write the failing test** `Loader_reads_guard_fields` (parse a two-control snippet through the loader's parse path; expose `internal static Bbs757ControlMap Parse(string json)` if there is none).
- [x] **Step 2-4:** run (FAIL), implement, full suite (PASS).
- [x] **Step 5: Commit** `feat(b757): control map carries a control's guard`.

### Task 7: The L:var snapshot tool

**Files:** Create `docs/b757/tools/lvar-snapshot/README.md`, `make_snap_js.py`, `snap.ps1`, `diff_snaps.py`.

Start from session 8's working scripts, already committed in `docs/b757/tools/live/` (`snap.js`, `snap.sh`, `diff_snaps.py`, `noise.txt`): this task adds the generator, a PowerShell entry point and noise learning, and leaves `live/` as it is.

- `make_snap_js.py <wasm> <lvars.tsv> <out.js>`: the printable strings (5+ chars) of `BBS752.wasm` matching `^(B757|BBS|BBS757|FMC|ENGINE|GO_AROUND|OVHD)_[A-Za-z0-9_]+$` plus the names in `docs/b757/evidence/package/lvars.tsv`, sorted, unique; writes one JS expression reading each as `L:<name>` "number" and returning JSON.
- `snap.ps1 -Out <file>`: `tools/coherent-eval.ps1 -Title BBSTablet -ExprFile <snap.js>`, saving the JSON.
- `diff_snaps.py <a> <b> [--noise <file>] [--learn-noise]`.
- README: session 8's use (`live-findings.md`), the noise-list step, and the rule that MSFSBA must not hold the `BBSTablet` view while it runs ([DBG-9]).
- [x] Write them, run once with the 757 loaded (owner) or against a saved JSON pair, commit `docs(b757): the L:var snapshot tool`. Then write this plan's last commit SHA into `00-index.md` "Base commits" (early plans) and push.

## In-sim checks (owner)

1. 757 loaded, BBS Nest running: open the CDU window (Shift+M), type a few keys, close it; switch to another aircraft and back. Nothing throws (startup.log / debug.log clean), the CDU window works after.
2. Near an ILS: no "glideslope alive" on the 757; on the PMDG 737 it still is.

## Handoff to plan 16

- **Pilot-facing:** "The 757 no longer says glideslope alive or lost: those came from the simulator's own nav radio, which BlueBird's 757 does not use, so they were wrong." Guide: the 757's automatic announcements; Ctrl+M loses its glideslope row on the 757. Changelog `fix`.
- **Proposed rule line:** `- [B757-5] Never read the stock NAV radio variables (frequency, OBS, CDI, glideslope) on the 757: BlueBird's VOR and ILS receivers are its own and leave them untouched (measured); frequencies come from the CDU's NAV RAD page through `Bbs757CduPages`, courses from `BBS_VOR_L_COURSE`, `BBS_VOR_R_COURSE`, `BBS_ILS_COURSE`. Full: docs/invariants/b757.md#b757-5`
- **docs/b757.md:** a "Seams" paragraph (`BBS757Definition.Hooks.cs`, the definition owning the tablet client, `Bbs757CduPages`: CDU 2 only, CLR held 1300 ms only to wipe), and the snapshot tool.
- **Frozen-file requests:** `MainForm.cs` (about line 1448, the exit path) says `DisposeBBS757Forms` disposes "the tablet client they share"; the definition disposes it now (`OnSwitchedAway`). `docs/invariants/coherent-clients.md#a380c-3`: the rule names a `_connectLock`; an on-demand call that never connects (the 757 tablet client's `ScrapeAsync` and `GetNavigraphAsync`) needs none: say "or never connects".
- **Ruling numbers clash:** Robin's prototype-plan commit 10e34ed18 added R51-R55 to `docs/b757/plans/01-prototype.md`, and `spec-gaps.md` uses R51-R74 for other decisions; plan 01's code cites R52 (`Bbs757CduPages`) and R56 (`BBS757Definition.UsesStockNavReceivers`, `Bbs757GlideslopeGateTests`). Renumber one set (owner's call) and the citations with it.
- **For the plans that use `Bbs757CduPages` (04, 06, 13):** it presses keys on CDU 2 only and reads either CDU. On a CDU 2 with no frame yet (after every aircraft load) call `ShowPageAsync` first: `EnterAsync` on it answers null. Calls are gated one at a time, not as a sequence: re-check the title of every screen you act on (`Bbs757CduPages.Title`). "The pilot is typing" is only `PendingKeyCount > 0`: a half-typed entry the pilot paused on CDU 2 is wiped by `EnterAsync`, which the guide should say (R52's risk, wider). The page wait is 3 s of wall clock and every wait stops once the tablet view is unreadable: measure a page key on the FO CDU just after a load in the sim. `DrainAsync` can report success for keys the client dropped in a reconnect within one 100 ms poll; `KeysDropped` is the signal.
- **Measured after landing (2026-10-26, `docs/b757/evidence/flight-08.md`):** one CLR held 1300 ms cleared the first officer's scratchpad in 45 of 60 runs; in the other 15 it removed only one character, and a second long CLR always cleared the rest. `Bbs757CduPages.WipeScratchpadAsync`'s fallback was one 250 ms CLR, which then left text, so the wipe answered false and `EnterAsync` refused. Fixed the same day at the owner's request (a plan 01 follow-up in the frozen `Bbs757CduPages.cs`): the fallback is a second CLR held 1300 ms (tests `Wipe_holds_clr_again_when_a_long_clr_removes_one_character`, `Wipe_answers_false_when_two_long_clrs_leave_text`, `Enter_types_after_a_long_clr_that_needed_a_second_hold`; the fake CDU now models a long CLR that removes one character). A wipe that needs both holds takes about 6 s.
- **For plans 08 and 09 (the CDU window):** the key queue is shared, so the window's scratchpad read-back waits while background keys are queued, and "keys not sent" after a link drop counts background keys too.
- **For plan 13:** `OnConnectionBatch`'s announcer may be `Suppressed` (a muted delivery can settle the BBS Nest check): speak later, not inline.
- **Review notes left unfixed (minor, for plan 16 to weigh):** the guard-fields test uses an invented node name and has no case of a guard without `close_sets`; `Bbs757ControlMap.Parse` repeats `LoadEmbedded`'s one-line deserialize; `ShowPage_presses_nothing_on_a_cdu_with_no_frame_while_unreachable` passes on the old code too; a transient null first read presses the page key even when that page is up; a drain timeout's recovery wipe can read before its CLR goes out (the call still answers null); `DrainAsync` can report keys the client dropped in a reconnect within one 100 ms poll as sent (`EnterAsync`'s scratchpad check still catches it); the page wait is exactly 3 s of wall clock, unmeasured against a real page change; the snapshot tool lets transport exceptions out raw, says "Python 3" where it needs 3.10, gives a traceback for a missing output folder, keeps an outdated comment on its count, and accepts a bare `{"id":1}` or an all-null snapshot; a section comment in `Bbs757SeamsTests.cs` lost a space.

## Execution record

Kept in this file only, so no other plan's push ever touches it. Claim the plan by filling in **Claimed by** and
setting **Status** to claimed, and push that one change before starting; if the push stops on a conflict in these
lines, someone claimed it first. Fill in the rest as you go and when the plan lands (index, "Records").

- **Status:** landed and pushed to `kn4iee/B757` (2026-10-26, cdf0b1fd7); in-sim checks passed
- **Claimed by:** kn4iee (the owner), 2026-10-26, when the work began. Plan 01 ran first and alone, before plans 02-16 gained their Task 0 claims (8a4cb6b74), so it has no Task 0 and its claim was not pushed on its own: it reaches the remote with the push of `B757`. No one else worked on it.
- **Worked by:** kn4iee with Claude: Tasks 1-4 inline (superpowers:executing-plans, test-first), Tasks 5-7 with subagents (superpowers:subagent-driven-development: an implementer and a task reviewer each, one fix round each for Tasks 5 and 7), then one whole-branch review and one fix wave; the owner ran the in-sim checks
- **Started:** 2026-10-26; `B757` at 2bcbed557, worked on `B757` directly (plan 01 ran alone)
- **Landed:** 2026-10-26; last code commit of the tasks 27ecc0894; merged with Robin's `kn4iee/B757` (25733526f: his EFB load and MCP speed fixes) and `origin/main` (f5ead55c2: 277 commits, Fenix v3 among them; conflicts in `MSFSBlindAssist.csproj`, both sides' resources kept, and the Takeoff Assist guide page, main's wording with the 757's 80/100 knots exception kept); record cf1dfce94; early plans' base line d93d7b5f9 (base cf1dfce94); in-sim results 416cfd09b (with plan 08's timing measurement); follow-up 9bce44b54 (the wipe's second long CLR). `git log --grep "^Plan: 01"` lists the plan's commits (416cfd09b carries `Plan: 08`).
- **Tests at landing:** full suite passed: 15,342 on 27ecc0894; 16,942 on the merge f5ead55c2; 16,944 after the wipe follow-up (9bce44b54); `MSFSBlindAssist.sln` builds with no new warning
- **Tasks:** all done.
  - Task 1, seams and shared keys: bea759f72.
  - Task 2, the tests split by owner: 29288150e (the loosening proved with a throwaway edit).
  - Task 3, the definition owns the tablet client: 94a57a5ed.
  - Task 4, no glideslope call-out from NAV 1: 7172efeb5.
  - Task 5, client additions and `Bbs757CduPages`: bc4d20185, fix round db146fadb (a CDU with no frame gets its page key).
  - Task 6, guard fields: 711afacc0.
  - Task 7, the snapshot tool: e7dcb6825, fix round 528a13691 (a debugger error is never saved as a snapshot); live run 2026-10-26.
  - Final review (no Critical; two Important, fixed): e3140beff (no client after a switch away; wall-clock page waits; presses on CDU 2 only; dispose in a `finally`; drain and trim fixes) and 27ecc0894 (the tool's outputs out of git).
- **Waiting on the owner:** nothing
- **In-sim results:** 2026-10-26, on the ground, the Debug build of the merge f5ead55c2. Check 1 passed (owner): the CDU window (Shift+M) read and typed on both CDUs; switched to the PMDG 737 with it open and back: it closed with nothing thrown, and afterwards the CDU window and the EFB window (Shift+T) worked; `debug.log` shows the tablet agent installed (v4), the switch away at 07:16:29 and back at 07:16:40, and no 757 error or warning (the SimConnect ILLEGAL_OPERATION lines while the PMDG 737 profile was selected over the loaded 757 also occur on 2026-10-22 and 2026-10-25, before this plan). Check 2 passed on the ground, as agreed with the owner instead of an ILS approach: the 757's Ctrl+M has no "Glideslope" row and the 737's has; the 757 now registers 501 variables (502 before), 29 of them batched (30 before). Task 7's live run: `snap.ps1` read all 2,173 variables in 0.2 s three times; `make_snap_js.py` regenerated `snap.js` byte-identical to `live/snap.js`; learning from idle snapshots added `B757_CREW_O2_BOTTLE` (1720 → 1730 at rest) to session 8's 65 noise names, after which an idle diff was empty.
- **Changed from the plan:** 35 hooks, not 36 (the list has 35). `BBS757Definition.CloseTabletWindows` (MainForm hands it in): the switch calls `OnSwitchedAway` before it disposes the 757's windows, so the definition closes the CDU and EFB windows itself before disposing the client. `CoherentBbs757TabletClient.IsDisposed` (internal, for the test). A switched-away definition never makes a client from the factory: it hands back a never-started, disposed one. `OnSwitchedAway` disposes the client in a `finally`. Added tests `Pmdg737_still_speaks_glideslope_alive_and_lost`, `Switching_away_makes_no_client`. The agent's version is checked on every scrape answer (`AgentIsCurrent`); `ScrapeAsync` never connects. The client implements `IBbs757CduLink`. `Bbs757CduPages`: presses on CDU 2 only (R52 in the seam), refuses empty or unkeyable text and non-keys, a wall-clock 3 s page wait that stops once the view is unreadable, `EnterAsync` and `PressKeysAsync` give up when their keys do not drain, a null first read still presses the page key (a frameless CDU 2). `Bbs757ControlMap.Parse(string)` added. The snapshot tool validates the answer before saving and ignores its outputs in git. After landing (2026-10-26, owner): the wipe's fallback is a second long CLR, from plan 08's measurement.
- **Rulings made while working:** (question; answer; who decided. Claude's rulings are the owner's to reverse.)
  - Claim and push before starting? Work on local `B757`, no separate claim push, no push without the owner; Claude, then the owner ("do not push yet").
  - The plan counts 36 hooks but lists 35? The 35 listed; Claude.
  - `SwitchAircraft` calls `OnSwitchedAway` before it disposes the 757's windows, in a frozen file: how do the CDU and EFB windows close before the client goes? MainForm hands the definition `CloseTabletWindows`, which `OnSwitchedAway` runs first; Claude.
  - How does a test see a sealed client disposed? An internal `IsDisposed`; Claude.
  - What does `EnsureTabletClient` give after a switch away? A never-started, disposed client, never a new socket (made complete after the final review); Claude.
  - How does the client know an old agent? The agent's scrape answer carries its version and `AgentIsCurrent` checks it, an old one is reinstalled; Claude.
  - May an on-demand scrape connect? No: it uses the loop's socket only ([A380C-3]); Claude.
  - How does `Bbs757CduPages` reach the client? The client implements `IBbs757CduLink`; Claude.
  - Inline or subagents? Tasks 1-4 inline, then subagents from Task 5; the owner (asked mid-plan).
  - Which final-review minor notes to fix? Only those in files frozen after plan 01 (CDU 2-only presses, the dispose in a `finally`, two comments, the drain and trim fixes, the tool's `.gitignore`); Claude.
  - `MainForm.cs`'s stale comment, in a frozen file? Handed to plan 16; Claude.
  - Task 6's commit names Opus though Sonnet wrote it? History left as it is; Claude.
  - "The pilot is typing" is only `PendingKeyCount > 0`, and the gate covers one call, not a sequence? Recorded in the handoff for plans 04 and 06, the seam left as it is; Claude.
  - Write the early plans' base commit and push? Written (cf1dfce94), not pushed; Claude, then the owner.
  - Merge Robin's `kn4iee/B757` and `origin/main`? Merged, both; the owner ("grab the latest main and merge").
  - In-sim check 2 needs an ILS approach the owner cannot fly yet? Checked on the ground instead (the Ctrl+M rows; the variable is no longer registered); proposed by Claude, run by the owner.
  - A long CLR sometimes removes one character (measured for plan 08): change the frozen `Bbs757CduPages`? A second long CLR as the wipe's fallback, now; the owner ("fix now").
