# BlueBird 757-200 Prototype Implementation Plan (plan 01)

**Claimed by:** Claude (overnight, 2026-10-25) · **Status:** built and reviewed (Tasks 1-7, final review and fix wave); in-sim tests pending ([01-prototype-insim-tests.md](01-prototype-insim-tests.md))

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A flyable prototype of the BlueBird 757-200 in MSFS Blind Assist: a selectable profile, every regular cockpit control in accessible panels, exact MCP value entry, a CDU window for both CDUs, a "757 EFB" window that operates BlueBird's tablet (Navigraph sign-in, AIRAC update, SimBrief ID, OFP fetch, cockpit presets), AI display reads, and a BBS Nest check.

**Architecture:** Controls come from a JSON control map GENERATED from the aircraft's cockpit XML (`tools/b757-gen`, embedded like the MD-11's map); the definition turns each row into a `SimVarDefinition` and writes by replaying the control's own click RPN through the MobiFlight calculator path. Two Coherent clients, one per Coherent view ([DBG-9]): the `BBSTablet` view (CDU screens from the Comm Bus, CDU key presses, the Navigraph device code) and the `BBSNavigraphMap` view (EFB clicks through the tablet's own `WASM_CLICK` handler). Everything sim-facing is verified by reading state back; pure parts get xUnit characterization tests.

**Tech Stack:** C# 13 / .NET 10 WinForms, Newtonsoft or System.Text.Json (whichever the MD-11 map loader uses), `System.Net.WebSockets.ClientWebSocket` (copy of the HS787 client), ES5 in-page agents, Python 3 stdlib for the generator, xUnit.

**Spec:** `docs/b757/spec.md` (read §1-§7 and "Prototype defaults"); evidence in `docs/b757/evidence/` (`cdu.md`, `efb-and-manual.md`, `live-findings.md`, `controls/`, `repo-touchpoints.md`).

## Global Constraints

- Profile code `BBS_757`; name "BlueBird 757-200"; menu text "&BlueBird 757-200". Class `BBS757Definition` in partials `MSFSBlindAssist/Aircraft/BBS757Definition*.cs`; helpers in `MSFSBlindAssist/Aircraft/BBS757/` (type prefix `Bbs757`); Coherent clients and pure parsers in `MSFSBlindAssist/SimConnect/BBS757/`; forms in `MSFSBlindAssist/Forms/BBS757/`; main-form partial `MSFSBlindAssist/MainForm.BBS757.cs`; agents `MSFSBlindAssist/Resources/coherent-bbs757-*.js`; tests `tests/MSFSBlindAssist.Tests/Bbs757*Tests.cs`.
- CLAUDE.md applies in full: read a file before changing it (CORE-16); build the `.sln` or pass `-p:Platform=x64` (CORE-1); typed numbers via `NumberParsing.TryParseTyped` (CORE-17); RPN numbers invariant fixed-point (VAR-5); speech per CORE-7; status text in read-only `TextBox`, never `Label` (VAT-13); `BeginInvoke` from background threads through a try/catch `SafeBeginInvoke` (A380C-6); US English.
- Calculator writes: `simConnect.ExecuteCalculatorCodeUnique(rpn)` (unique prefix, [SIM-17]); before ANY write check `simConnect.CalcWriteCanLand` and, when false, speak `"{label} unavailable"` and write nothing ([MD11-16] pattern). Register `MSFSBA_BRIDGE_PROBE` (quiet OnRequest LVar) so the calc path is verified.
- Coherent: views resolved by TITLE CONTENT (`BBSTablet`, `BBSNavigraphMap`), never by `VCockpit` number (renumbered on every load); one socket per view, claimed with `CoherentViewOwnership.Claim` ([DBG-9]); re-install the agent on a still-open socket and Abort+Dispose before reconnect ([A380C-2]); agents are ES5 with a top-level try/catch and their own `<None Update>` csproj entry ([HS-4]).
- MCP targets: heading `L:BBS_AP_TARGET_HEADING` (direct write), altitude `L:BBS_AP_TARGET_ALTITUDE` (direct write, steps 100 ft), speed `L:BBS757_ORANGE_AIRSPEED_BUGS` (READ ONLY: set by firing `K:AP_SPD_VAR_INC`/`DEC` the difference, 1 kt each), V/S `L:BBS_AP_TARGET_VS` (unmeasured: direct write, read back, else step `K:AP_VS_VAR_INC`/`DEC`). Never read MCP values from stock autopilot SimVars.
- CDU: Comm Bus events `BBS_CDU_DISPLAY_1` / `_2`, JSON `{"Target":"Display","Data":[336 cells]}`, 14 rows × 24 columns, cell `[]` or `[char, colorLetter, size(0 large/1 small), inverse]`; captain's latest frame also on `document.querySelector('bbs-tablet').lastMobiFlightCduPayload`. Keys `L:B757_CDU_<1|2>_<KEY>`: write 1, hold 250 ms, write 0; next key ≥ 400 ms later; CLR is never held longer than 250 ms (a 1 s CLR clears the whole scratchpad).
- EFB: gauge `efb_l` (1397 × 1048) inside the `BBSNavigraphMap` view; a click is `onMouseMove`, `OnMouseDown`, `OnMouseUp`, `onClick` on its `<wasm-sim-canvas>` with `clientX = rect.left + x`, `clientY = rect.top + y`. Positions are BlueBird 0.1.0's layout (table in Task 7).
- BBS Nest: process name `bbs_nest`.
- Never write BlueBird's settings files; never display or log the Hoppie code, SayIntentions key or any Navigraph token. Only open Navigraph URLs whose host is `navigraph.com` or `identity.api.navigraph.com`.
- Build: `dotnet build MSFSBlindAssist.sln -c Debug`. Tests: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`. Both must pass before every commit that touches C#.

## Review Focus

- The sim, BBS Nest, MobiFlight or the Coherent debugger is missing: every window and hotkey says so plainly; nothing throws; clients retry quietly (tests in Tasks 3, 6, 7).
- The AIRAC update must never click when the installed cycle is current: BlueBird shows REMOVE where UPDATE was (Task 7 test `AiracUpdate_refuses_when_installed_is_current`).
- A malformed or short CDU frame (fewer than 336 cells, empty JSON, unknown glyphs) yields 14 padded lines, never an exception (Task 6 test).
- Typed values under de-DE (`"250,5"` / `"10.000"`) parse as the pilot means and go out invariant (Task 5 test with `CultureInfo("de-DE")`).
- An aircraft reload renumbers the Coherent views and drops the agents: both clients re-resolve and re-install without the pilot doing anything (Task 6 and 7 client loops; checked in sim).

---

### Task 1: Control-map generator and the embedded map

**Files:**
- Move: `docs/b757/tools/extract_controls.py` → `tools/b757-gen/extract_controls.py` (`git mv`)
- Create: `tools/b757-gen/generate_b757_map.py`, `tools/b757-gen/curation.json`, `tools/b757-gen/test_generate_b757_map.py`
- Create: `MSFSBlindAssist/Resources/bbs757_control_map.json` (generated, committed)

**Interfaces:**
- Produces the JSON schema every later task reads:
  ```json
  { "aircraft": "bluebirdsim-aircraft-757-200", "package_version": "0.1.0",
    "controls": [ { "node": "B757_OVHD_BATTERY_BUTTON", "section": "Overhead", "panel": "Electrical",
      "label": "Battery", "kind": "toggle", "state_var": "B757_OVHD_BATTERY_BUTTON",
      "positions": [ { "value": 0, "label": "Off" }, { "value": 1, "label": "On" } ],
      "press": "<rpn>", "release": null, "hold_ms": 0, "cw": null, "ccw": null } ],
    "status": [ { "key": "B757_APU_STATE", "panel": "Status", "label": "APU",
      "positions": [ {"value":0,"label":"Off"}, {"value":1,"label":"Starting"}, {"value":2,"label":"Running"}, {"value":3,"label":"Shutting down"} ],
      "announce": true } ] }
  ```
  `kind` ∈ `toggle`, `switch` (multi-position), `button` (momentary), `held` (momentary with `hold_ms`), `select`, `guard`. `state_var` may be null for a button with no L:var state. RPN fields are the control's expanded click code, unclipped.
- `extract_controls.py` gains `extract(pkg: pathlib.Path) -> tuple[list[dict], list[dict], dict]` returning the unclipped control rows, light rows and summary data; its CLI output is unchanged.

- [x] **Step 1: Write failing unittest cases** in `test_generate_b757_map.py` against small in-memory rows: `humanize("B757_OVHD_BATTERY_BUTTON") == "Battery"`; `humanize("B757_OVHD_APU_GEN_BUTTON") == "APU Gen"` (acronyms APU, IRS, VHF, HF, ADF, VOR, DME, EEC, CVR, EICAS, CRT, TCAS, ILS, GPS, FMC, CDU, MCP, EFIS, ND, PFD, AT, AP, FD, GND, ENG, ELEC, HYD, L, R, C, BTL stay upper); `positions_for` turns ANIMTIP `["BATTERY OFF","BATTERY ON"]` into labels `Off`/`On` (common leading words removed) and a clamp `range 0..2 step 1` without tips into `Position 0..2`; a circuit breaker, CDU key, lever, infinite knob, finite knob with more than 12 positions and any row in an excluded banner are dropped; labels inside one panel are unique (second "Light" becomes "Light 2").
- [x] **Step 2: Run** `python -I -m unittest tools/b757-gen/test_generate_b757_map.py` from the repo root. Expected: failures (module missing).
- [x] **Step 3: Implement `generate_b757_map.py`** (`python -I generate_b757_map.py <package> [--out path]`, default out `MSFSBlindAssist/Resources/bbs757_control_map.json`; finds the package itself in `Community`/`Community2024` under `%LOCALAPPDATA%\Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalCache\Packages` when no path is given). Rules: kinds from the extractor (`toggle`→toggle, `multi-position switch`→switch, `rotary knob finite` with ≤ 12 positions→switch, `momentary button`→button, `held button`→held with `hold_ms` from the template (default 1500), `select button`→select, `guard`→guard); a `switch` whose click steps with clamps keeps `cw`/`ccw` (or `press` when it cycles); section from the node prefix (`B757_OVHD_`/`BBS_OVHD_` Overhead, `B757_MCP_` Glareshield, `B757_MAIN_`/`B757_ADI_`/`B757_HSI_` Forward panel, `B757_PED_` Pedestal, else Other); panel from `curation.json` `panels` (vendor banner → app panel name), falling back to the banner in Title Case; `curation.json` also holds `exclude_banners` (CB OVERHEAD PANEL, the two CDU banners, chairs, window rain, misc parts, yokes, throttles, rudder trim, emissive gauges), `labels` (node → label overrides; fix at least the ALT knob, AUTORATE and fire handle slips named in `evidence/controls/summary.md`), and `status` (the status rows: `B757_APU_STATE` as above; `BBS_CA_MASTER_WARNING` and `BBS_CA_MASTER_CAUTION` Off/On announce true; `B757_L_BUS_PWR_SRC`, `B757_R_BUS_PWR_SRC`, `B757_APU_RPM_PRC`, `B757_DEBUG_PRESSURE_CABIN_ALT_FT` announce false, no positions). Sort controls by section, panel, file line.
- [x] **Step 4: Run the unittests** — all pass. Then generate the real map: `python -I tools/b757-gen/generate_b757_map.py`. Expected: a JSON with 300-600 controls; spot-check the battery, STBY POWER (3 positions), APU selector (OFF/ON/START), L FWD fuel pump, F/D toggles, the MCP mode buttons (`button`, `press`+`release`), the yoke A/P disconnect (`held`).
- [x] **Step 5: Commit** `feat(b757): control-map generator and the generated map` (git add the tools folder, the JSON and the move).

### Task 2: Map model, loader and click-RPN builders (pure)

**Files:**
- Create: `MSFSBlindAssist/Aircraft/BBS757/Bbs757ControlMap.cs`, `MSFSBlindAssist/Aircraft/BBS757/Bbs757Rpn.cs`
- Modify: `MSFSBlindAssist/MSFSBlindAssist.csproj` (one `<EmbeddedResource Include="Resources\bbs757_control_map.json" />` beside the MD-11's)
- Test: `tests/MSFSBlindAssist.Tests/Bbs757ControlMapTests.cs`, `tests/MSFSBlindAssist.Tests/Bbs757RpnTests.cs`

**Interfaces:**
- Produces:
  - `public sealed class Bbs757ControlMap { List<Bbs757Control> Controls; List<Bbs757StatusRow> Status; static Bbs757ControlMap Load(); }` — embedded resource found by `EndsWith("bbs757_control_map.json")`, cached in a `Lazy<>` (ExecutionAndPublication), a bad or missing resource gives an empty map plus `Log.Error`, never a throw (copy `Md11ControlMap.LoadEmbedded`).
  - `public sealed class Bbs757Control { string Node; string Section; string Panel; string Label; string Kind; string? StateVar; List<Bbs757Position> Positions; string? Press; string? Release; int HoldMs; string? Cw; string? Ccw; }`, `public sealed record Bbs757Position(double Value, string Label)`, `public sealed class Bbs757StatusRow { string Key; string Panel; string Label; List<Bbs757Position> Positions; bool Announce; }`, `public static class Bbs757Kinds` (string constants for the six kinds).
  - `public static class Bbs757Rpn`:
    - `string? ToggleTo(Bbs757Control c, double current, double target)` — the press code when `current != target`, else null.
    - `IReadOnlyList<string> StepTo(Bbs757Control c, double current, double target)` — `|target-current|` copies of `Cw` (up) or `Ccw` (down) joined into calls of at most 20 steps each; when the control has only `Press` and cycles, `(target-current) mod positionCount` copies of `Press`; empty when already there.
    - `string SetLVar(string lvar, double value)` — `"{value:0.######} (>L:{lvar})"` invariant.
    - `IReadOnlyList<string> KeyBurst(string kEvent, int count)` — `count` copies of `(>K:{kEvent})`, at most 25 per string.

- [x] **Step 1: Write failing tests.** `Load_finds_the_battery`: the map has more than 200 controls and a `toggle` `B757_OVHD_BATTERY_BUTTON` labelled `Battery` with positions Off/On and a press code containing `>L:B757_OVHD_BATTERY_BUTTON`. `Every_control_has_a_writer`: each control has `Press`, or `Cw`+`Ccw`. `Labels_are_unique_per_panel`. `ToggleTo_returns_null_when_already_there`. `StepTo_builds_two_ccw_steps` for a 3-position switch from 2 to 0. `StepTo_splits_long_bursts` (45 steps → 3 strings). `SetLVar_is_invariant_under_de_DE` (`CultureInfo.CurrentCulture = new("de-DE")`; `SetLVar("X", 1.5)` == `"1.5 (>L:X)"`). `KeyBurst_caps_25_per_call`.
- [x] **Step 2: Run** with `--filter Bbs757` — fails to compile.
- [x] **Step 3: Implement both files** with the signatures above.
- [x] **Step 4: Run the filtered tests, then the full suite** — all pass.
- [x] **Step 5: Commit** `feat(b757): control-map loader and click-RPN builders`.

### Task 3: The definition and app registration

**Files:**
- Create: `MSFSBlindAssist/Aircraft/BBS757Definition.cs` (identity, `BuildVariables`, panels), `MSFSBlindAssist/Aircraft/BBS757Definition.Interaction.cs` (`HandleUIVariableSet`, `ProcessSimVarUpdate`, `HandleHotkeyAction` with the Ctrl+M case and `return base.HandleHotkeyAction(...)`), `MSFSBlindAssist/Aircraft/BBS757/Bbs757Nest.cs`, `MSFSBlindAssist/Forms/BBS757/BBS757MonitorManagerForm.cs`, `MSFSBlindAssist/MainForm.BBS757.cs`, `.claude/rules/b757.md`, `docs/b757.md`
- Modify (exact lines in `docs/b757/evidence/repo-touchpoints.md` §4 and the registration report folded into this plan): `MainForm.Designer.cs` (field, construction, AddRange, property block "&BlueBird 757-200"), `MainForm.AircraftSwitch.cs` (`LoadAircraftFromCode` `"BBS_757" => new BBS757Definition()`, `UpdateAircraftMenuItems` uncheck/check, `SwitchAircraft` calls `DisposeBBS757Forms()` unconditionally after the iFly teardown), `MainForm.Announcers.cs` (Ctrl+M mute gate for `BBS_757`), `Settings/UserSettings.cs` (`BBS757DisabledMonitorVariables` + `[JsonIgnore] BBS757DisabledMonitorVariablesSet` + rebuild line, comment "eight"), `Services/DefAnnounceMuteSets.cs` (`"BBS_757" => settings.BBS757DisabledMonitorVariablesSet`), `Forms/HotkeyListForm.cs` (`{ "BBS_757", "BBS757_Hotkeys.txt" }`), `CLAUDE.md` (one "Where things live" row: `| [b757.md](docs/b757.md) | BlueBird 757-200: generated control map, calculator writes, CDU over the Comm Bus, EFB by scripted clicks | b757 |`), `MSFSBlindAssist/HotkeyGuides/BBS757_Hotkeys.txt` (create; general sections copied from `iFly_737MAX8_Hotkeys.txt` without the First Officer line; 757 sections for Shift+M CDU, Shift+T EFB, Ctrl+H/S/A/V MCP entry, Shift+H/S/A/V readouts, Alt+P/N/E/S display reads, Ctrl+M) plus its csproj `<None Update>` entry
- Tests: `tests/MSFSBlindAssist.Tests/Bbs757DefinitionTests.cs`; add the 757 to `ComboLabelCollapseTests.AllAircraft`, `FirstOfficer/FirstOfficerWindowsTests` (`["BBS_757"] = null`), `CalcPathProbeOptInTests.AircraftThatWriteThroughTheCalculator`, `DefAnnounceMuteSetsTests` (Marked + InlineData), and a `ClaudeContextBudgetTests` InlineData row for `bbs757_control_map.json` like line 143's MD-11 row

**Interfaces:**
- Consumes: Task 2's `Bbs757ControlMap`, `Bbs757Rpn`.
- Produces:
  - `public partial class BBS757Definition : BaseAircraftDefinition` with `AircraftName => "BlueBird 757-200"`, `AircraftCode => "BBS_757"`; `internal Bbs757ControlMap Map`; `internal SimConnect.SimConnectManager? Sim` captured on first `HandleUIVariableSet`/`ProcessSimVarUpdate`.
  - Variables: one per control keyed by `Node` (Name = `StateVar`, LVar, `UpdateFrequency.OnRequest`, `ValueDescriptions` from positions; `RenderAsButton = true` and `SuppressRestingButtonState = true` for button/held, also when `StateVar` is null — then key the def on a harmless OnRequest read of the node name); one per status row (`Continuous`, `IsAnnounced = Announce`, read-only `RenderAsReadOnlyStatus`, `ExcludeFromMonitorManager = !Announce`); `MSFSBA_BRIDGE_PROBE` (as the MD-11 registers it).
  - Panels: sections and panels from the map in map order, plus a "Status" panel under section "Status".
  - `HandleUIVariableSet`: unknown key → `base`; `!sim.CalcWriteCanLand` → speak `"{label} unavailable"`; toggle/guard/select → `ReadFreshAsync(node, 1500)` then `ToggleTo`; switch → read current, `StepTo`, send each string with `ExecuteCalculatorCodeUnique`; button → press, `await Task.Delay(200)` (UI thread), release, then speak `"{label} pressed"`; held → same with `HoldMs`; afterwards `RequestVariable(node, forceUpdate: true)` after 1500 ms so the combo shows the aircraft's state. Returns true when handled.
  - `ProcessSimVarUpdate`: on its first call per connection, `Bbs757Nest.IsRunning()` false → speak once "BBS Nest is not running. The 757's systems will not respond until it runs and the flight is reloaded."; otherwise return false (generic announcer speaks announced status rows).
  - `public static class Bbs757Nest { static bool IsRunning(Func<string, bool>? processExists = null); }` (default `Process.GetProcessesByName("bbs_nest").Length > 0`).
  - MainForm: `ShowBBS757MonitorManagerDialog()`, `DisposeBBS757Forms()`, `BBS757MenuItem_Click`.

- [x] **Step 1: Write failing tests** `Bbs757DefinitionTests`: code and name; every `GetPanelControls()` key is in `GetVariables()`; the battery var is LVar `B757_OVHD_BATTERY_BUTTON` with descriptions Off/On; the probe var exists and is OnRequest, not announced; the "Status" panel lists `B757_APU_STATE`; `Bbs757Nest.IsRunning(_ => false)` is false. Plus the fleet-test additions listed above.
- [x] **Step 2: Run** the filtered tests — fail.
- [x] **Step 3: Implement** the files and edits. Rule file `.claude/rules/b757.md` front matter globs: `MSFSBlindAssist/Aircraft/BBS757*.cs`, `MSFSBlindAssist/Aircraft/BBS757/**`, `MSFSBlindAssist/Forms/BBS757/**`, `MSFSBlindAssist/MainForm.BBS757.cs`, `MSFSBlindAssist/Resources/bbs757_control_map.json`, `tests/MSFSBlindAssist.Tests/**/*Bbs757*.cs`, `tools/b757-gen/**` (add `SimConnect/BBS757/**` and `Resources/coherent-bbs757*.js` in the tasks that create them); body: heading and "Loaded when Claude reads matching code. Background: docs/b757.md. No rules yet: add the first as CLAUDE.md's "Adding or changing a rule" says." `docs/b757.md`: a short aircraft doc (transport, BBS Nest, CDU, EFB, where the map comes from, regenerate with `tools/b757-gen`).
- [x] **Step 4: Build the solution and run the full suite** — all pass, including `ClaudeContextBudgetTests`.
- [x] **Step 5: Commit** `feat(b757): BlueBird 757-200 profile with generated panels`.

### Task 4: AI display reads

**Files:**
- Create: `MSFSBlindAssist/Aircraft/BBS757/Bbs757DisplayReads.cs`
- Modify: `MSFSBlindAssist/Services/GeminiService.cs` (four `DisplayType` members `Adi757`, `Ehsi757`, `UpperEicas757`, `LowerEicas757` and their prompts in `GetPromptForDisplay`, MD-11 wording style: "The image may contain several displays. ONLY describe the …", "Do not use markdown"), `MSFSBlindAssist/Aircraft/BBS757Definition.cs` (`protected override IReadOnlyList<AiDisplayRead> DisplayReads => Bbs757DisplayReads.All;`)
- Test: `tests/MSFSBlindAssist.Tests/Bbs757DisplayReadsTests.cs`

**Interfaces:**
- `Bbs757DisplayReads.All`: `ReadDisplayPFD` → `Adi757` "ADI" view 0; `ReadDisplayND` → `Ehsi757` "EHSI" view 0; `ReadDisplayUpperECAM` → `UpperEicas757` "Upper EICAS" view 1; `ReadDisplayLowerECAM` → `LowerEicas757` "Lower EICAS" view 1 (instrument views measured: index 0 ADI/EHSI, 1 EICAS).

- [x] **Step 1: Failing tests:** the four rows with those actions, types, names and views; each new prompt names the 757 and says which display to describe (copy the assertions style of `Md11DisplayReadsTests` and `DisplayPromptTests`).
- [x] **Step 2: Run** — fail. **Step 3: Implement.** **Step 4: Full suite** — pass.
- [x] **Step 5: Commit** `feat(b757): AI display reads from the measured instrument views`.

### Task 5: MCP value entry and readouts

**Files:**
- Create: `MSFSBlindAssist/Aircraft/BBS757/Bbs757Mcp.cs` (pure), `MSFSBlindAssist/Aircraft/BBS757Definition.Mcp.cs`
- Test: `tests/MSFSBlindAssist.Tests/Bbs757McpTests.cs`

**Interfaces:**
- `Bbs757Mcp` constants: `HeadingVar = "BBS_AP_TARGET_HEADING"`, `AltitudeVar = "BBS_AP_TARGET_ALTITUDE"`, `SpeedVar = "BBS757_ORANGE_AIRSPEED_BUGS"`, `VsVar = "BBS_AP_TARGET_VS"`, `MachModeVar = "BBS_AIRSPEED_IN_MACH"`; `static IReadOnlyList<string> SpeedSteps(double current, double target)` → `Bbs757Rpn.KeyBurst("AP_SPD_VAR_INC"|"AP_SPD_VAR_DEC", |round(target)-round(current)|)`; `static string HeadingWrite(int deg)` (0-359, 360 → 0), `static string AltitudeWrite(int ft)` (rounded to 100).
- Definition: `HandleHotkeyAction` cases `FCUSetHeading`, `FCUSetSpeed`, `FCUSetAltitude`, `FCUSetVS` open a modeless `ValueInputForm` the HS787 way (validators `FcuValueEntry.TryHeading`/`TrySpeed`/`TryAltitude`; V/S −8000..6000 in 100 steps) after `hotkeyManager.ExitInputHotkeyMode()`; on value: refuse aloud unless `CalcWriteCanLand`; heading/altitude direct write; speed: if Mach mode read 1 speak "MCP is in Mach mode. Set Mach from the Glareshield panel." else read `SpeedVar` fresh and send `SpeedSteps`; V/S: direct write, read back after 1 s, if unchanged step `AP_VS_VAR_INC`/`DEC` by the difference / 100. After each, read back after 1 s and speak "Heading 090" / "Speed 250" / "Altitude 12000" / "Vertical speed minus 1500" from the read value (the window value, not the request). `ReadHeading`/`ReadSpeed`/`ReadAltitude`/`ReadFCUVerticalSpeedFPA` speak the same phrases from fresh reads.

- [x] **Step 1: Failing tests:** `SpeedSteps(200, 250)` is 2 strings totalling 50 INC; `SpeedSteps(250, 199)` is 51 DEC; `SpeedSteps(200, 200)` empty; `HeadingWrite(360) == "0 (>L:BBS_AP_TARGET_HEADING)"`; `AltitudeWrite(12049) == "12000 (>L:BBS_AP_TARGET_ALTITUDE)"`; under de-DE, `NumberParsing.TryParseTyped("250", …)` then `FcuValueEntry.TrySpeed(value, out _, out _)` accepts 250, and `HeadingWrite`/`AltitudeWrite`/`SpeedSteps` output contains no comma.
- [x] **Step 2-4:** run (fail), implement, full suite (pass).
- [x] **Step 5: Commit** `feat(b757): exact MCP heading, speed, altitude and V/S entry`.

### Task 6: CDU window over the Comm Bus

**Files:**
- Create: `MSFSBlindAssist/SimConnect/BBS757/Bbs757CduFrame.cs` (pure), `MSFSBlindAssist/SimConnect/BBS757/CoherentBbs757TabletClient.cs`, `MSFSBlindAssist/Resources/coherent-bbs757-tablet-agent.js`, `MSFSBlindAssist/Forms/BBS757/BBS757CduForm.cs` (+ `.Designer.cs`)
- Modify: `MSFSBlindAssist/MSFSBlindAssist.csproj` (agent `<None Update>`), `MSFSBlindAssist/MainForm.BBS757.cs` (`ShowBBS757CduDialog()`; dispose in `DisposeBBS757Forms`), `MSFSBlindAssist/MainForm.Hotkeys.cs` (Shift+M branch `AircraftCode == "BBS_757"` before the A320 branch), `.claude/rules/b757.md` (globs for `SimConnect/BBS757/**`, `Resources/coherent-bbs757*.js`)
- Test: `tests/MSFSBlindAssist.Tests/Bbs757CduFrameTests.cs`

**Interfaces:**
- `public sealed record Bbs757CduScreen(IReadOnlyList<string> Lines)`; `public static class Bbs757CduFrame { static Bbs757CduScreen Parse(string? json); }` — 14 lines of exactly 24 characters, blanks as spaces, `☐` as `_`, missing or bad JSON → 14 blank lines.
- Agent (ES5, marker `MSFSBA_BBS757_TABLET_INSTALLED`, global `window.__MSFSBA_BBS757`): on install registers `RegisterViewListener('JS_LISTENER_COMM_BUS')` and `.on('BBS_CDU_DISPLAY_1'|'_2')` storing the raw JSON and a counter; seeds CDU 1 from `bbs-tablet.lastMobiFlightCduPayload`; `scrape(cdu)` → `{ok:true, seq, frame}`; `press(cdu, key)` → writes `L:B757_CDU_{cdu}_{key}` 1 then 0 after 250 ms via `SimVar.SetSimVarValue`; `navigraph()` → `{signedIn: !!t.ngAccessToken, userCode, uri}` from `bbs-tablet.nav_device_auth` (never return tokens).
- `CoherentBbs757TabletClient`: copy of `CoherentHS787CduClient` (needle `"BBSTablet"`, agent file above, `CoherentViewOwnership.Claim` like `CoherentA32nxMcduClient`), events `ScreenUpdated(int cdu, Bbs757CduScreen screen)`; `int ActiveCdu { get; set; }` (1 or 2; polling scrapes only the active one, 350 ms visible / 1000 ms idle); `void PressKey(int cdu, string key)` queued with 400 ms spacing; `Task<(bool SignedIn, string? UserCode, string? Uri)> GetNavigraphAsync()`.
- `BBS757CduForm`: clone of `HS787FMCForm` (ListBox lines, scratchpad TextBox, same shortcuts: Ctrl+1-6 / Alt+1-6 LSKs or F-keys per `MCDUUseAlternateLSKKeys`, PageUp/PageDown PREV/NEXT, Ctrl+Enter EXEC, Alt+page letters for INIT REF (`INIT`), RTE, DEP ARR (`DEPARR`), ATC, VNAV, FIX, LEGS, HOLD, FMC COMM (`FMCCOMM`), PROG, MENU, NAV RAD (`NAVRAD`); typing maps letters `KEYA`-`KEYZ`, digits `NUM0`-`NUM9`, `.` `NUMPER`, `+`/`-` `NUMPLUS`, space `KEYSP`, `/` `KEYSLASH`; Backspace CLR, Delete DEL) plus Ctrl+Tab to switch captain/first officer CDU (spoken "Captain CDU" / "First officer CDU"); scratchpad announcements through `CduScratchpadAnnouncer`; a read-only status TextBox saying "Waiting for the 757's CDU. Is the aircraft powered?" until the first frame.

- [x] **Step 1: Failing tests** for `Parse`: the IDENT frame from `docs/b757/evidence/cdu.md` (use a fixture JSON with the 14 lines) gives line 0 `"          IDENT         "` and line 13 starting `NAV DATA OUT OF DATE`; a 100-cell frame pads to 14 × 24; `"{}"` and `null` give 14 blank lines; `☐` becomes `_`.
- [x] **Step 2-4:** run (fail), implement parser, client, agent, form, wiring; full suite (pass).
- [x] **Step 5: Commit** `feat(b757): CDU window for both CDUs over the Comm Bus`.

### Task 7: The 757 EFB window

**Files:**
- Create: `MSFSBlindAssist/SimConnect/BBS757/Bbs757EfbLayout.cs` (pure), `MSFSBlindAssist/SimConnect/BBS757/Bbs757WorkFolder.cs` (pure with injectable roots), `MSFSBlindAssist/SimConnect/BBS757/AiracCycle.cs` (pure), `MSFSBlindAssist/SimConnect/BBS757/CoherentBbs757EfbClient.cs`, `MSFSBlindAssist/Resources/coherent-bbs757-efb-agent.js`, `MSFSBlindAssist/Forms/BBS757/BBS757EfbForm.cs`
- Modify: csproj (agent entry), `MainForm.BBS757.cs` (`ShowBBS757EfbDialog()`, dispose), `MainForm.Hotkeys.cs` (Shift+T branch for `BBS_757`)
- Test: `tests/MSFSBlindAssist.Tests/Bbs757EfbTests.cs`

**Interfaces:**
- `Bbs757EfbLayout` (EFB-local points, `record EfbPoint(int X, int Y)`; measured unless marked "est."): `Home (19,62)`; home icons `Aircraft (320,190)`, `Navigation (503,190)`, `Settings (1073,190)`; Settings menu `AiracData (95,486)`; AIRAC page `SimBriefField (842,126)`, `UpdateButton (1291,337)`; keypad `7 (823,255) 8 (936,255) 9 (1052,255) 4 (823,334) 5 (936,334) 6 (1052,334) 1 (823,413) 2 (936,413) 3 (1052,413) 0 (936,489) BKSP (823,565) DEL (936,565) OK (1052,565)`; Navigation menu `AcarsDashboard (135,179)`; dashboard `FetchOfp (441,120)`; units dialog `UnitsYes (390,658)`, `UnitsNo (993,658)` est.; presets (Aircraft app opens on COCKPIT PRESETS) `ColdAndDark (433,214)` est., `Turnaround (433,266)` est., `BeforeStart (433,319)`, `ReadyForTaxi (433,372)` est., `ReadyForTakeoff (433,424)` est., `AfterTakeoff (818,214)` est., `Cruise (818,266)` est., `Approach (818,319)` est., `FinalApproach (818,372)` est.; quick start `ApuQuickStart (433,524)`, `EngineQuickStart (818,524)` est., `IrsRapidAlign (1197,524)` est. Sequences: `IReadOnlyList<(EfbPoint Point, int DelayAfterMs)>` builders `OpenAiracPage()`, `EnterSimBriefId(string digits)` (field, digits 500 ms apart, OK), `FetchOfp()`, `LoadPreset(string name)`, `QuickStart(string name)`; every sequence starts with `Home` and 1500 ms.
- `AiracCycle`: `static int ForDate(DateOnly d)` anchored on 2501 effective 2025-01-23, 28-day cycles, numbering restarting each calendar year at the first cycle effective in that year (2610 effective 2026-10-01).
- `Bbs757WorkFolder`: `static string? Find(IReadOnlyList<string> roots)` over `%LOCALAPPDATA%\Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalState\WASM\MSFS2020\bluebirdsim-aircraft-757-200\work`, the same with `MSFS2024`, `%APPDATA%\Microsoft Flight Simulator 2024\WASM\MSFS2020\…\work`, `%LOCALAPPDATA%\Packages\Microsoft.FlightSimulator_8wekyb3d8bbwe\LocalState\packages\bluebirdsim-aircraft-757-200\work`, `%APPDATA%\Microsoft Flight Simulator\Packages\bluebirdsim-aircraft-757-200\work`; `static long? ReadSimBriefId(string folder)` (`simbrief_id`, −1 or missing → null); `static int? ReadInstalledCycle(string folder)` (`NavigationData\cycle.json`, the cycle number field; tolerate unknown shapes → null). Never read or return any other settings value.
- Agent (ES5, marker `MSFSBA_BBS757_EFB_INSTALLED`, global `window.__MSFSBA_BBS757EFB`): `click(x, y)` dispatches the four canvas calls; `clickSeq(json)` runs a `[[x,y,delay]…]` list with `setTimeout`.
- `CoherentBbs757EfbClient` (needle `"BBSNavigraphMap"`, claim the view, no polling): `Task<bool> RunAsync(IReadOnlyList<(EfbPoint, int)> seq, CancellationToken ct)` — false when the view or agent is unavailable.
- `BBS757EfbForm`: buttons "Sign in to Navigraph" (tablet client `GetNavigraphAsync`; signed in → "Already signed in to Navigraph"; else speak "Navigraph code" then the code letter by letter and open the URI in the default browser only when its host is `navigraph.com` or `identity.api.navigraph.com`), "Update nav data" (installed = `ReadInstalledCycle`; refuse with "Nav data is current, cycle {n}" when installed ≥ `AiracCycle.ForDate(today)`; else `OpenAiracPage`, wait 4 s, click `UpdateButton`, poll the cycle file up to 20 s, speak "Nav data cycle {n} installed. Reload the flight to use it." or "The update did not finish. Try again after reloading the flight."), "SimBrief ID" box + "Set SimBrief ID" (digits only; after the sequence read `ReadSimBriefId` and speak "SimBrief ID saved" or "BlueBird did not save the SimBrief ID"), "Fetch SimBrief flight" (sequence, then "SimBrief flight requested. If BlueBird asks to switch weight units, use Accept units or Keep units. Then run ACARS INIT FLT in the CDU window."), "Accept units" / "Keep units", a preset combo + "Load preset" (speaks "Loading {preset}"), "APU quick start", "IRS rapid align"; a read-only status TextBox; every action refuses aloud when the EFB view is unreachable ("The 757's EFB is not reachable. Is the aircraft loaded?").

- [x] **Step 1: Failing tests:** `AiracCycle.ForDate(2026-10-01) == 2610`, `(2026-09-30) == 2609`, `(2026-01-22) == 2601`, `(2025-12-31) == 2513`; `AiracUpdate_refuses_when_installed_is_current` (a pure `Bbs757EfbLayout.ShouldUpdate(int? installed, DateOnly today)` false for 2610 on 2026-10-25, true for null and 2603); `EnterSimBriefId("189")` is Home, Settings, AIRAC, field, 1, 8, 9, OK in order; `Find` picks the first existing root (temp dirs); `ReadInstalledCycle` reads a fixture `cycle.json` and returns null for garbage; `ReadSimBriefId` maps −1 to null.
- [x] **Step 2-4:** run (fail), implement, full suite (pass).
- [x] **Step 5: Commit** `feat(b757): EFB window operating BlueBird's tablet`.

### Task 8: Finish: guide stub, docs, in-sim test plan, push

**Files:**
- Modify: `docs/b757.md` (complete: what works, how, limits), `docs/b757/plans/01-prototype.md` (tick steps, Status), `docs/b757/spec.md` §8 if new facts
- Create: `docs/b757/plans/01-prototype-insim-tests.md` — one check per line for the owner (Robin, blind: nothing needs sight): load at a gate with BBS Nest running → no Nest warning; select the profile; Before Start preset from the EFB window; battery/APU/fuel pump/pack from the panels with read-back; MCP heading/speed/altitude entry and readouts; CDU window both sides, MENU → ACARS → INIT FLT → INIT; route REQUEST, LOAD, ACTIVATE, EXEC; Navigraph sign-in button; nav data update refusal (current) ; SimBrief ID; fetch; each estimated EFB position; AI reads Alt+P/N/E/S; reload the flight and check the windows recover.

- [x] **Step 1: Release build** `dotnet build MSFSBlindAssist.sln -c Release` and the full test suite — pass.
- [x] **Step 2: Request a whole-branch code review** (superpowers:requesting-code-review) and fix what it confirms.
- [x] **Step 3: Commit and push** to `kn4iee/B757`.

### Task 9: Fuel and payload from SimBrief (added 2026-10-25, owner's request)

An instant load that reproduces BlueBird's own SimBrief loading (measured: `evidence/live-findings.md`, session 5),
through the measured setters. No typing box (the owner's call).

**Files:**
- Create: `MSFSBlindAssist/SimConnect/BBS757/Bbs757Loadsheet.cs` (pure: parse the plan, compute the loads, phrase the
  result), tests `tests/MSFSBlindAssist.Tests/Bbs757LoadsheetTests.cs` with a fixture `Fixtures/bbs757-simbrief-ofp.xml`
  (made-up airline, flight and IDs; the measured numbers below)
- Modify: `MSFSBlindAssist/Services/SimBriefService.cs` (one small method that returns the latest OFP's XML for a
  SimBrief user ID or a username, reusing its HttpClient), `MSFSBlindAssist/Forms/BBS757/BBS757EfbForm.cs` (button
  "Load fuel and payload from SimBrief"), `MSFSBlindAssist/MainForm.BBS757.cs` (seams for the writes and reads),
  `MSFSBlindAssist/Aircraft/BBS757Definition.cs` (OnRequest stock reads it needs), hotkey guide, `docs/b757.md`, the
  guide's `#boeing-757-efb`, `docs/b757/plans/01-prototype-insim-tests.md`

**Interfaces and values:**
- Plan fields (SimBrief XML): `fuel/plan_ramp`; `weights/pax_count_actual` (else `pax_count`), `weights/pax_weight`,
  `weights/payload`, `weights/cargo`; `params/units` (`lbs` or `kgs`: kilograms × 2.20462); `general/icao_airline` +
  `general/flight_number`, `origin/icao_code`, `destination/icao_code` for the spoken line.
- Source: BlueBird's stored SimBrief ID (`Bbs757WorkFolder.ReadSimBriefId`, as `userid=`), else the app's
  `SimbriefUsername` setting; neither → "Set your SimBrief ID first." Never log or speak the ID.
- Loads (pounds; constants from `flight_model.cfg` and the measurement): wing tank 14,579 (2,176 gal × 6.7), center
  46,390 (6,924 gal × 6.7), seats 176 (16 + 160), forward hold 10,300, aft hold 14,700.
  - Fuel: each wing `min(block / 2, 14579)`, center the rest; center above 46,390 → refuse "The plan's fuel is more
    than the tanks hold." and write nothing.
  - Passengers: `min(pax, 176) × pax_weight`, written as one total (`L:BBS_EFBX_SET_PAX_WEIGHT_LBS`: BlueBird splits
    First and Economy).
  - Cargo: `max(0, payload − passenger weight)`, half to each hold, the forward hold capped at 10,300 and its excess
    aft; more than 25,000 → refuse "The plan's cargo is more than the holds take." and write nothing.
  - Measured case to pin in a test: block 25,550, 224 × 175, payload 51,459, cargo 12,259 → wings 12,775 each,
    center 0, passengers 30,800, forward 10,300, aft 10,359. Also: block 40,000 → wings 14,579, center 10,842.
- Writes: one calculator string through `ExecuteCalculatorCodeUnique` (refuse aloud unless `CalcWriteCanLand`):
  `L:BBS_EFBX_SET_FUEL_LEFT_LBS`, `_FUEL_RIGHT_LBS`, `_FUEL_CENTER_LBS`, `_PAX_WEIGHT_LBS`, `_CARGO_FWD_LBS`,
  `_CARGO_AFT_LBS`, invariant numbers (VAR-5). Only on the ground (fresh `SIM ON GROUND`, as the presets).
- Read-back about 3 s later and speak from it: `FUEL TANK LEFT/RIGHT/CENTER QUANTITY` × `FUEL WEIGHT PER GALLON`,
  `PAYLOAD STATION WEIGHT:3`+`:4` (passengers), `:5`, `:6`. Phrase, rounded to 10, in the aircraft's units
  (`Bbs757WorkFolder.ReadUnits`; pounds when unread): "Loaded BVI 4RK, KSEA to KDEN. Fuel 25550 pounds: left 12780,
  right 12780, center 0. Passengers 30800 pounds. Cargo 20660 pounds: forward 10300, aft 10360." When the plan has
  more passengers than seats, add "The plan's 224 passengers are capped at the 757's 176 seats; the rest of the
  payload goes in the holds." Then: "For the ACARS weight and balance, request WGTBAL on the CDU."

- [ ] **Step 1: Failing tests** for the parser (fixture, kilogram plan, missing fields → null), the loads (the
  measured case, the 40,000 case, both refusals, zero cargo, fewer passengers than seats), the write string (invariant
  under de-DE, the six setters), the phrase (de-DE, kilograms, the cap sentence).
- [ ] **Step 2-4:** run (fail), implement, full suite (pass).
- [ ] **Step 5: Commit** `feat(b757): load fuel and payload from SimBrief`.

## Rulings made while executing (2026-10-25)

Decisions the plan left open, taken overnight so the run did not stop. Each says what it costs if it is wrong; the
owner can reverse any of them.

- R1: the C# loader maps the generator's snake_case JSON names explicitly. If wrong: the map loads empty (tests catch it).
- R2: Task 5 routes the MCP hotkeys through `BBS757Definition.Interaction.cs`. If wrong: one routing edit.
- R3: `MainForm.BBS757.cs` owns one tablet client and one EFB client, shared by the windows ([DBG-9]). If wrong: a
  second client would fight for the view.
- R4, R14: the BBS Nest warning speaks once per connection, about a second after load even in a quiet cockpit. If
  wrong: a late or repeated warning.
- R5, R19, R20, R21: the nav data update clicks only when the installed cycle is older than today's, the tablet page
  loaded on or after the new cycle's first full day and at least a minute ago, Navigraph was signed in for the whole
  page load, the click lands on the page that was judged (once per page), and the newest-written work folder is the
  one read; the SimBrief ID entry clears the field first and reads the stored ID back. If wrong: refusals on a cycle's
  first day, or (lapsed subscription, late cycle) a click on REMOVE, which the app reports.
- R6: Task 8 pushes to `kn4iee/B757` without asking again.
- R7: the vendor's click code is replayed verbatim, suspected slips only listed. If wrong: eight fuel-pump legend
  writes stay as broken as the cockpit's own click.
- R8: a switch with no click code is written through its state variable. If wrong: those switches ignore the write
  (the read-back shows it).
- R9: the cabin altitude knob is left out of the map (it writes 0 and 7 and rests at 4); the generator now checks
  every written value against the positions. If wrong: one pressurization knob missing until measured.
- R10: the 757 rule file, doc stub and CLAUDE.md row came in Task 2 (the budget test needs them for any new code).
- R11: a test keeps the 757 under 900 registered variables (the SimConnect ceiling is 1000).
- R12: the engine start selectors take positions 1 GND, 3 AUTO, 4-6 unnamed (their click code skips 2). If wrong:
  4-6 read as numbers until measured.
- R13: MCP speed entry is IAS only; a Mach entry is refused aloud. If wrong: Mach is set from the Glareshield panel.
- R15, R17: a speed set reads back, corrects up to three rounds and speaks the value read. If wrong: a set takes up to
  a few seconds longer.
- R16: implementers verified their agents live with page keys and navigation clicks only.
- R18: typed "-" is one +/- press, "+" two (Boeing behavior, unmeasured on BlueBird).
- R22: the app makes no Takeoff Assist 80/100-knot calls on the 757 (spec default 7). If wrong: only the aircraft's own
  calls are heard.
- R23: Accept units / Keep units read BlueBird's units setting before and after and say which units are set. If wrong:
  a click on an unmeasured spot when no dialog is up, reported as unchanged.
