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

- [ ] **Step 1: Write the failing test** `Bbs757SeamsTests.Hooks_file_declares_every_seam`: read `MSFSBlindAssist/Aircraft/BBS757Definition.Hooks.cs` from the repo root (as other source-scan tests find files) and assert each of the 36 method names above appears with `partial void`; `Monitor_rows_without_hooks_are_todays` (`MonitorRows()` equals `MonitorRowBuilder.Build(GetVariables())`); `Shared_keys_are_distinct` (three constants, distinct, prefix `BBS757_`); `Display_override_unchanged_without_hooks` (the 757's `TryGetDisplayOverride` gives today's text for the squawk row and the flap row).
- [ ] **Step 2: Run** `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter Bbs757SeamsTests`. Expected: FAIL.
- [ ] **Step 3: Implement** the hooks file (a class comment naming each group's plan), the shared keys and the calls; each call site gets a one-line comment naming the plan whose file implements it.
- [ ] **Step 4: Run the full suite.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(b757): seams for the gap plans`.

### Task 2: Split the test files by owner

**Files:**
- Modify: `tests/MSFSBlindAssist.Tests/Bbs757DefinitionTests.cs`, `tests/MSFSBlindAssist.Tests/Bbs757FlightReadinessTests.cs`
- Create: `tests/MSFSBlindAssist.Tests/Bbs757McpWindowTests.cs`

**Interfaces:**
- `Bbs757DefinitionTests` (frozen after this): `Sections_and_panels_follow_the_map_order` checks that the map's panels appear in each section, and the map's controls in each panel, **in map order as a subsequence** (app panels and fields may sit between); `Every_status_row_is_on_the_status_display_then_the_flight_controls` checks the map's status rows then the flight-control rows appear in that order (other rows may follow); `The_pedestal_starts_with_the_control_stand_the_engines_and_the_transponder` checks the order of those three among the Pedestal's map panels.
- Move verbatim, keeping each test's name, from `Bbs757FlightReadinessTests.cs` to the new `Bbs757McpWindowTests.cs` (owned by plan 05, then 14): the sections "The autopilot window's table", "Ctrl+P's A/T Arm: BlueBird's armed flag" and "The MCP value windows' buttons", and the three window-press tests `A_window_press_the_aircraft_refused_says_so_once`, `A_window_press_that_took_is_silent`, `A_window_press_that_cannot_land_is_refused_aloud_and_sends_nothing`, with the fixture helpers they need (copied, not shared).

- [ ] **Step 1: Make the edits** (no production change).
- [ ] **Step 2: Run the full suite.** Expected: PASS, with the same test count as before.
- [ ] **Step 3: Prove the loosening:** a throwaway local edit that inserts a dummy app panel after "TCAS and transponder" in `BuildPanelsOnce` keeps the three tests green; revert it.
- [ ] **Step 4: Commit** `test(b757): split the 757 tests by plan owner`.

### Task 3: The definition owns the tablet client

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/BBS757Definition.cs`, `MSFSBlindAssist/MainForm.BBS757.cs`, `MSFSBlindAssist/Aircraft/BBS757Definition.Interaction.cs` (`OnSwitchedAway`)
- Test: `tests/MSFSBlindAssist.Tests/Bbs757SeamsTests.cs`

**Interfaces:**
- Produces on the definition: `internal CoherentBbs757TabletClient EnsureTabletClient()` (created and started on first use, the client MainForm used to own), `internal Func<CoherentBbs757TabletClient>? TabletClientFactory { get; init; }` (tests replace it), and disposal at the end of `OnSwitchedAway` (after the windows close).
- `MainForm.EnsureBbs757TabletClient()` returns `(currentAircraft as BBS757Definition)?.EnsureTabletClient()` and throws nothing when the 757 is not loaded (it returns null; its callers already only run on the 757: make them null-safe); `DisposeBBS757Forms` stops disposing the tablet client.

- [ ] **Step 1: Write the failing tests:** `Tablet_client_is_created_once` (two `EnsureTabletClient` calls, one factory call); `Switching_away_disposes_it_after_the_windows` (a fake client records Dispose after the definition's windows close).
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the full suite** (`CoherentBbs757TabletClientTests`). Expected: PASS.
- [ ] **Step 5: Commit** `refactor(b757): the 757 definition owns the tablet client`.

### Task 4: No glideslope call-out from the simulator's NAV 1 (a fix)

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/BaseAircraftDefinition.cs` (the variable named `NAV HAS GLIDE SLOPE:1`, about line 175, and the alive/lost speech, about lines 775-786), `MSFSBlindAssist/Aircraft/BBS757Definition.cs` (one override)
- Test: `tests/MSFSBlindAssist.Tests/Bbs757GlideslopeGateTests.cs`

**Interfaces:**
- `public virtual bool UsesStockNavReceivers => true;` on `BaseAircraftDefinition` (doc comment: false for an aircraft whose own receivers leave the sim's NAV radios untouched); `BBS757Definition` overrides it `false`. When false, `GetBaseVariables` does not register the glideslope row (no Ctrl+M row, [VAR-9]) and the alive/lost branch never speaks.

- [ ] **Step 1: Write the failing tests:** `Bbs757_registers_no_glideslope_row`; `Pmdg737_keeps_its_glideslope_row` (old key, `IsAnnounced` unchanged); `Bbs757_never_speaks_glideslope_alive` (the base key fed 1 then 0: nothing).
- [ ] **Step 2: Run** with `--filter Bbs757GlideslopeGateTests`. Expected: FAIL.
- [ ] **Step 3: Implement** (comment: spec 3.2).
- [ ] **Step 4: Run the full suite** (`DefAnnounceMuteSetsTests`, `ClaudeContextBudgetTests`). Expected: PASS.
- [ ] **Step 5: Commit** `fix(b757): no glideslope call-out from the sim's NAV 1 on the 757`.

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

- [ ] **Step 1: Write the failing tests.** Client: `PressExpression_carries_the_hold` (exact string; 5000 → 1500; 10 → 100); `Agent_press_takes_a_hold_argument` (source scan of the agent); `Agent_version_check` (`AgentIsCurrent(AgentVersion - 1)` false, `(AgentVersion)` true, `(null)` false). Pages, against a fake link with scripted screens: `ShowPage_presses_nothing_when_already_there`; `ShowPage_presses_the_page_key_and_waits` (LEGS then NAV RADIO: one `NAVRAD`); `Enter_types_wipes_and_presses_the_line_key` (`EnterAsync(2, "113.90", "L1")`: `KEYCLR`/1300, `NUM1 NUM1 NUM3 NUMPER NUM9 NUM0`, `L1`, in order); `Enter_presses_no_line_key_when_a_message_covers_the_typing` (`INVALID ENTRY`: no `L1`, a second wipe, null); `Enter_refuses_while_the_pilot_types` (`PendingKeyCount` 3); `PressKeys_sends_the_keys_in_order` (`KEYDEL`, `L1`); `Calls_never_throw`; `Title_and_page_count` (`"  ACT RTE 1 LEGS     1/7"` → `"ACT RTE 1 LEGS"`, (1, 7), identity `"RTE 1 LEGS"`, spoken `"ACT RTE 1 LEGS 1/7"`; `"  MOD RTE 1 LEGS     1/7"` has the same identity; `"        NAV RADIO       "` → no count).
- [ ] **Step 2: Run** both filters. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the full suite** (`Bbs757CduKeysTests` reads the agent's key list: unchanged). Expected: PASS.
- [ ] **Step 5: Commit** `feat(b757): the first officer's CDU as a page service`.

### Task 6: Map loader guard fields

**Files:** Modify `MSFSBlindAssist/Aircraft/BBS757/Bbs757ControlMap.cs`; test `Bbs757SeamsTests.cs`.

**Interfaces:** `Bbs757Control` gains `public string? Guard { get; set; }` (the guard control's node), `public double? GuardClosed { get; set; }`, `public double? CloseSets { get; set; }` (the value BlueBird writes to this control when the guard closes, or null), read from JSON `guard`, `guard_closed`, `close_sets` in the loader's explicit snake_case mapping (ruling R1); absent → null.

- [ ] **Step 1: Write the failing test** `Loader_reads_guard_fields` (parse a two-control snippet through the loader's parse path; expose `internal static Bbs757ControlMap Parse(string json)` if there is none).
- [ ] **Step 2-4:** run (FAIL), implement, full suite (PASS).
- [ ] **Step 5: Commit** `feat(b757): control map carries a control's guard`.

### Task 7: The L:var snapshot tool

**Files:** Create `docs/b757/tools/lvar-snapshot/README.md`, `make_snap_js.py`, `snap.ps1`, `diff_snaps.py`.

Start from session 8's working scripts, already committed in `docs/b757/tools/live/` (`snap.js`, `snap.sh`, `diff_snaps.py`, `noise.txt`): this task adds the generator, a PowerShell entry point and noise learning, and leaves `live/` as it is.

- `make_snap_js.py <wasm> <lvars.tsv> <out.js>`: the printable strings (5+ chars) of `BBS752.wasm` matching `^(B757|BBS|BBS757|FMC|ENGINE|GO_AROUND|OVHD)_[A-Za-z0-9_]+$` plus the names in `docs/b757/evidence/package/lvars.tsv`, sorted, unique; writes one JS expression reading each as `L:<name>` "number" and returning JSON.
- `snap.ps1 -Out <file>`: `tools/coherent-eval.ps1 -Title BBSTablet -ExprFile <snap.js>`, saving the JSON.
- `diff_snaps.py <a> <b> [--noise <file>] [--learn-noise]`.
- README: session 8's use (`live-findings.md`), the noise-list step, and the rule that MSFSBA must not hold the `BBSTablet` view while it runs ([DBG-9]).
- [ ] Write them, run once with the 757 loaded (owner) or against a saved JSON pair, commit `docs(b757): the L:var snapshot tool`. Then write this plan's last commit SHA into `00-index.md` "Base commits" (early plans) and push.

## In-sim checks (owner)

1. 757 loaded, BBS Nest running: open the CDU window (Shift+M), type a few keys, close it; switch to another aircraft and back. Nothing throws (startup.log / debug.log clean), the CDU window works after.
2. Near an ILS: no "glideslope alive" on the 757; on the PMDG 737 it still is.

## Handoff to plan 16

- **Pilot-facing:** "The 757 no longer says glideslope alive or lost: those came from the simulator's own nav radio, which BlueBird's 757 does not use, so they were wrong." Guide: the 757's automatic announcements; Ctrl+M loses its glideslope row on the 757. Changelog `fix`.
- **Proposed rule line:** `- [B757-5] Never read the stock NAV radio variables (frequency, OBS, CDI, glideslope) on the 757: BlueBird's VOR and ILS receivers are its own and leave them untouched (measured); frequencies come from the CDU's NAV RAD page through `Bbs757CduPages`, courses from `BBS_VOR_L_COURSE`, `BBS_VOR_R_COURSE`, `BBS_ILS_COURSE`. Full: docs/invariants/b757.md#b757-5`
- **docs/b757.md:** a "Seams" paragraph (`BBS757Definition.Hooks.cs`, the definition owning the tablet client, `Bbs757CduPages`: CDU 2 only, CLR held 1300 ms only to wipe), and the snapshot tool.

## Execution record

Kept in this file only, so no other plan's push ever touches it. Claim the plan by filling in **Claimed by** and
setting **Status** to claimed, and push that one change before starting; if the push stops on a conflict in these
lines, someone claimed it first. Fill in the rest as you go and when the plan lands (index, "Records").

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
