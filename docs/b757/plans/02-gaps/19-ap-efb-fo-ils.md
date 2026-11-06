# BlueBird 757: Yoke A/P Disconnect, EFB Page Check, First Officer Tests, Lights and Pace, ILS by Variable (plan 19, added 2026-11-05)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0 is the controller's own, done before Task 1 is dispatched.

**Goal:** Six owner requests of 2026-11-05: the Ctrl+P window disconnects with the yoke's button; the EFB window stops saying a page did not show when it did; the First Officer runs BlueBird's preflight tests, manages every exterior light at the right flow, and sets switches as fast as the other aircraft's; the ILS frequency is read from BlueBird's own variable instead of a picture, and N says the VOR frequencies from variables when the CDU page cannot be read.

**Architecture:** Eight independent pieces on the existing seams: `Bbs757AutopilotLayout` (Ctrl+P), `Bbs757EfbScreen.ParseMenu` and `Bbs757EfbPressRunner.ArriveAsync` (EFB), `BBS757Definition.FoSetCoreAsync` (pace), the First Officer's flow, checklist and executor tables (lights, tests), and the ILS display seam `IBbs757IlsDisplay` (a variable reader replaces the picture reader) plus `Bbs757NavRad.Readout` (VORs).

**Tech Stack:** C# 13 / .NET 10 Windows Forms, xUnit, SimConnect through MobiFlight calculator writes.

**Spec:** the owner's design of 2026-11-05, approved that day, kept locally (`docs/superpowers/specs/2026-11-05-b757-ap-efb-fo-ils-design.md`, git-ignored by the owner's choice); every decision and value a task needs is copied below. Evidence: `docs/b757/evidence/flight-19.md` (Task 0 commits it). Rules: `.claude/rules/b757.md`, `first-officer.md`, `first-officer-boeing.md`, `first-officer-automation.md`, `variable-definitions.md` (they load when you Read the code).

## Global Constraints

- Branch `B757` in the main folder (no worktree). Every commit carries the trailers `Plan: 19` and `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Nothing is pushed until Task 10.
- Tests: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --artifacts-path bin/b757-p19 --filter "FullyQualifiedName~<name>"` (the artifacts path spares the app's exe); the full suite the same without `--filter`. Never build the bare csproj without `-p:Platform=x64` (CORE-1).
- Test names keep `Bbs757` (the b757 rule glob). No test reads a file under `docs/b757/` (plan 16 deletes it).
- CORE-7: never repeat what the screen reader said; a press with no readable state says "{label} pressed" once. CORE-17: a typed or external number parsed with `NumberParsing`. Every new spoken number is formatted invariant and tested under `sv-SE` (`TestCulture.Run`, as the existing 757 tests do).
- VAR-9: a variable read only for a readout or a window is `ExcludeFromMonitorManager = true`, OnRequest, on no panel.
- FO-5 (the 2 s pause between steps) is never touched. FO-6/FO-7: every new flow step that writes is `Done(...)`-linked to a checklist line of its flow's group, and every new actionable line has a delivering step. FO-13: a gear check stays the last step of its flow. FO-19: the 10,000 ft landing-light switching is unchanged. FO-25: tests run only behind the Preflight flow's existing on-ground and engines-stopped guards.
- File ownership (`00-index.md`): plans 05, 10, 12, 14, 17, 18 have landed, so plan 19 takes over the files it names; the pilot-facing docs (`docs/b757.md`, `HotkeyGuides/BBS757_Hotkeys.txt`, `guide/**`, the rule lines) stay plan 16's: Task 9 writes their text as handoff requests.

## Review Focus

1. The ILS variable reads 0 (a BlueBird build without it, MSFS 2020 unchecked) or nothing: the app falls back to its click count's words, never "ILS 0.00" — Task 5, `Bbs757IlsPanelTests.Reading_from_an_absent_variable_is_none`.
2. A VOR frequency variable that is 0 or out of 108.00-117.95 (not tuned): "VOR L not tuned", never "VOR L 0.00" — Task 6, `Bbs757NavRadTests.Variables_out_of_the_vor_band_are_not_tuned`.
3. A switch that reads its target once and then snaps back (BlueBird's REALISTIC parking brake behaves so): not landed — Task 4, `Bbs757FoWriteTests.A_target_read_once_then_lost_is_not_landed`.
4. A navigation tap that really did not land (the old section still shown) still says "The tablet did not show …"; an unreadable page ("Unknown page") does not — Task 3, the existing `A_menu_press_whose_page_never_comes_says_so` plus `A_menu_press_whose_page_reads_unknown_says_nothing`.
5. The flow's Stop during a test's hold still releases the button or the P61 switch — Task 8, `Bbs757FoExecutorTests.A_stop_during_a_test_still_releases_it`.

---

### Task 0 [controller]: Claim, evidence, fixtures

- [ ] **Step 1:** Write `docs/b757/evidence/flight-19.md` from the design's Evidence and Measured sections: the EFB tour (2026-11-05, 7 of 14 side-menu taps and both app pages read with no section; the normalized band gap 10.7-13.6 against the threshold 12; white text 118 of 255), the ILS and VOR variables measured at cruise (`BBS_757_ILS_FREQ` -1 parked, 108.1, 108.15, back to -1; `BBS_757_ILS_CRS` follows `BBS_ILS_COURSE`; `BBS_757_VOR_L_FREQ` 114.5 then 116.05, `_R_FREQ` 115.1 then 116.4; `B757_MCP_VOR1/2_MODE_AUTO` 1), the public simvars table (support.bluebirdsim.com/docs/simvars, checked against source 21 Oct 2026, 1,235 L:vars; the six `BBS_757_*` nav variables are not in it, they are in the updated `BBS752.wasm`), the owner's report that a second yoke A/P disconnect press silences the warning, and the lighting sources (the table in Task 7).
- [ ] **Step 2:** Commit this plan and the evidence: `docs(b757): plan 19 and its evidence` with the trailers.

### Task 1: Ctrl+P disconnects with the yoke's button

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/BBS757/Bbs757AutopilotLayout.cs`, `MSFSBlindAssist/Aircraft/BBS757Definition.Autopilot.cs` (summary comment only)
- Test: `tests/MSFSBlindAssist.Tests/Bbs757McpWindowTests.cs`

**Interfaces:**
- Produces: `Bbs757AutopilotLayout.AutopilotDisconnectNode = "btn_yoke_autopilot_disengage_pilot"` (the map's "Captain Yoke A/P Disconnect", kind held, the vendor's 1500 ms hold). `DisengageBarNode` is removed with its uses; `SwitchNodes` and `RequestedKeys` lose it.

- [ ] **Step 1: Tests.** In `Bbs757McpWindowTests`: replace `Ctrl_p_disengage_bar_shows_its_position_and_flips` with `Ctrl_p_ap_disconnect_presses_the_captains_yoke_button` (the button labelled "A/P Disconnect" exists, its state is "", `RequestedKeys` does not contain the bar's node, a press writes the yoke node's press code (contains `btn_yoke_autopilot_disengage_pilot` and `K:AUTOPILOT_OFF`) and then its release, and says exactly `"A/P Disconnect pressed"`); add `Bbs757AutopilotLayout.AutopilotDisconnectNode` to the `unlit` set of `Every_autopilot_button_...` and let that test accept kind held for it; add `[InlineData(Bbs757AutopilotLayout.AutopilotDisconnectNode, "A/P Disconnect pressed")]` to `Ctrl_p_press_with_no_light_says_pressed_once`. Keys stay `"LCRAFOTSNVHDEYGPIXB"` (Alt+B moves to A/P Disconnect).
- [ ] **Step 2:** Run the filter `Bbs757McpWindowTests`: the new tests FAIL.
- [ ] **Step 3: Implement.** `Nodes` ends with `SpeedInterveneNode, AutothrottleDisconnectNode, AutopilotDisconnectNode`; its key is `'B'`; the caption "A/P Disconnect" is an entry for `"btn_yoke_autopilot_disengage_pilot"` in `Bbs757McpWindowLayout`'s `Overrides` table (read by `CaptionOverride`, which `AutopilotButtons` already uses; the map label "Captain Yoke A/P Disconnect" stays on the panel). Add `Bbs757McpWindowLayout.cs` to this task's files. `PressFromWindowAsync` already presses a momentary/held control and says "… pressed" when it has no MCP light: confirm the held press goes through `PressAsync` with the map's hold. Update the class summaries (the window's buttons, "the disengage bar" removed; "a second press silences the warning, the owner, 2026-11-05").
- [ ] **Step 4:** Run the filter `Bbs757McpWindow`: PASS.
- [ ] **Step 5:** Commit `feat(b757): Ctrl+P's A/P Disconnect is the captain's yoke button, in place of the disengage bar`.

### Task 2: EFB: the selected side-menu entry on a dim tablet

**Files:**
- Modify: `MSFSBlindAssist/SimConnect/BBS757/Bbs757EfbScreen.cs` (`ParseMenu`, `Bbs757EfbScreenModel`), `MSFSBlindAssist/SimConnect/BBS757/Bbs757EfbBridge.cs` (its per-read debug line)
- Create: `tests/MSFSBlindAssist.Tests/Fixtures/bbs757-efb/dim2/*.png` and `*.lines.json` (16 pages, copied from the session scratchpad `C:\Users\augus\AppData\Local\Temp\claude\246dffd62b838557\9ea2ced9-158c-4fa4-9e81-ff60a270faaa\scratchpad\fixtures-dim2`, each name's `---` made `-`: `01-home`, `02-aircraft`, `03-aircraft-cockpit-presets`, … `16-navigation-perf-calc`)
- Test: `tests/MSFSBlindAssist.Tests/Bbs757EfbScreenTests.cs`

**Interfaces:**
- Produces: `internal static int Bbs757EfbScreen.SelectedEntry(IReadOnlyList<double> bands)` (the index of the selected entry, or -1); `Bbs757EfbScreenModel.MenuBands` (`IReadOnlyList<double>`, init, empty by default: each menu entry's band value as `ParseMenu` measured it, in menu order).

- [ ] **Step 1: Tests.** `Dim_tablet_pages_read_their_section` (Theory over the dim2 fixtures, through the existing `Load("dim2", name)`): `01-home` "Home"; `02-aircraft` "Aircraft, Cabin & doors"; `03-aircraft-cockpit-presets` "Aircraft, Cockpit presets"; `04-aircraft-fuel` "Aircraft, Fuel"; `05-aircraft-weight-balance` "Aircraft, Weight & balance"; `06-aircraft-ground-services` "Aircraft, Ground services"; `07-aircraft-cabin-doors` "Aircraft, Cabin & doors"; `08-aircraft-maintenance` "Aircraft, Maintenance"; `09-aircraft-failures` "Aircraft, Failures"; `10-navigation` "Navigation, ACARS dashboard"; `11-navigation-acars-dashboard` "Navigation, ACARS dashboard"; `12-navigation-operating-fp` "Navigation, Operating FP"; `13-navigation-weather-notam` "Navigation, Weather & NOTAM"; `14-navigation-charts` "Navigation, Charts"; `15-navigation-map` "Navigation, Map"; `16-navigation-perf-calc` "Navigation, PERF calc". (Today 9 of them read the app alone.) And `Selected_entry_stands_out_from_the_others`: `SelectedEntry([16.9, 16.5, 5.4, 16.9, 17.3])` is 2; `[16.9, 16.5, 16.9, 17.3]` is -1; `[16.9, 10.0, 5.4, 16.9]` is -1 (two stand out); `[30, 30]` is -1; `[]` is -1; one bright-tablet case with a gap of 40 is selected.
- [ ] **Step 2:** Run the filter `Bbs757EfbScreenTests`: the dim2 cases FAIL (9 of 16), the rule test fails to compile.
- [ ] **Step 3: Implement `SelectedEntry`** in place of the fixed `bestGap = 12` loop: the median of the bands, each band's gap from it; the largest gap is the selection when it is at least `SelectedMinGap = 6` and at least `SelectedOutlierRatio = 3` times the second largest; else -1. `ParseMenu` uses it, records the bands on the model (`MenuBands`), and is otherwise unchanged (the CHECKLIST exclusions stay).
- [ ] **Step 4:** Run the whole filter `Bbs757Efb`: PASS, every existing dim and bright fixture still reading the section it read (no existing assertion changed).
- [ ] **Step 5: The log line.** In `Bbs757EfbBridge`'s "EFB reader: …" debug line, when the model has a menu, its `Section` is empty and its `App` is not `HOME`, append `, no menu entry selected, bands ` and the bands joined with `/`, each `0.0` invariant. No test (a log line); build.
- [ ] **Step 6:** Commit `fix(b757): the EFB reads the selected side-menu entry on a dim tablet`.

### Task 3: EFB: no "did not show" for a page whose section was not read

**Files:**
- Modify: `MSFSBlindAssist/SimConnect/BBS757/Bbs757EfbPressRunner.cs` (`ClickAsync`, `ArriveAsync`)
- Test: `tests/MSFSBlindAssist.Tests/Bbs757EfbBridgeTests.cs`

**Interfaces:**
- Produces: `internal static bool Bbs757EfbPressRunner.ShowsOtherPage(Bbs757EfbScreenModel now, Bbs757EfbItem pressed, Bbs757EfbScreenModel pressedOn)`: true only when the read names another place than the tap asked for. Home (`btn:HOME`): `now.App` not empty and not `HOME`. A home-screen app (`app:X`): `now.App` not empty and not X. A side-menu entry (`menu:`): `now.App` not empty and either another app than `pressedOn.App`, or the same app with a non-empty `Section` that `PageIs` does not match. False for an empty `App` ("Unknown page") and for the right app with an empty `Section`.

- [ ] **Step 1: Tests** in the `A_menu_press_...` group: `A_menu_press_whose_section_is_not_read_says_nothing` (the FakeTablet shows `AIRCRAFT` with section `""` after the tap: no alert, one tap, the runner still reads for `NavigateMaxMs`); `A_menu_press_whose_page_reads_unknown_says_nothing` (app `""`: no alert); `A_home_press_that_lands_in_another_app_says_so` (Home tapped, the read stays `NAVIGATION`: the alert "The tablet did not show Home. Try again, or use Read the tablet again."). `A_menu_press_whose_page_never_comes_says_so` (the section stays GROUND SERVICES) is unchanged and must still pass, in both cultures.
- [ ] **Step 2:** Run the filter `Bbs757EfbBridgeTests`: the two "says nothing" tests FAIL.
- [ ] **Step 3: Implement:** `ArriveAsync(Bbs757EfbScreenModel after, Bbs757EfbItem item, Bbs757EfbScreenModel pressedOn, CancellationToken ct)` reads again as now until `PageIs` or `NavigateMaxMs`; when it never matched, it logs as now and alerts `NotShownText(page)` only when `ShowsOtherPage(now, item, pressedOn)`; otherwise it logs `EFB reader: tapped '{page}', page reads '{now.PageName}'; its section was not read, nothing said`. `ClickAsync` passes `item` and `target.Page`. Update the class summary's item 4 ("…until the page name is the one tapped; a read that shows another app or another section says so, one whose section or app could not be read says nothing").
- [ ] **Step 4:** Run the filter `Bbs757Efb`: PASS.
- [ ] **Step 5:** Commit `fix(b757): the EFB does not call a page missing when only its section was not read`.

### Task 4: First Officer: set and move on

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/BBS757Definition.FirstOfficer.cs` (`FoSetCoreAsync`, `FoSetParkingBrakeCoreAsync`)
- Modify: `tests/MSFSBlindAssist.Tests/Bbs757GuardedTests.cs` (`Bbs757FakeIo` gains `Func<string, double?>? OnFreshRead`, consulted before `Fresh` when set, so a test can answer a sequence)
- Test: `tests/MSFSBlindAssist.Tests/FirstOfficer/BBS757/Bbs757FoWriteTests.cs`

**Interfaces:**
- Produces: `internal const int FoSettleMs = 150, FoPollMs = 250, FoLandMaxMs = 3000` on `BBS757Definition`; `FoSetCoreAsync(Bbs757Control control, double target, Bbs757SimIo io, CancellationToken ct, bool pollForLanding = true)`.

- [ ] **Step 1: Tests.** `A_switch_that_lands_at_once_is_done_in_two_reads` (the toggle follows its press: after the write the waits are exactly `[150, 250]` and the result is true); `A_switch_that_lands_late_is_polled_until_it_holds` (reads after the write answer 0, 0, 1, 1: true, waits `[150, 250, 250, 250]`); `A_target_read_once_then_lost_is_not_landed` (1, 0, 0, …: false after the 3,000 ms budget, waits summing to at most 3,000); `A_switch_that_never_lands_gives_up_at_three_seconds` (always 0: false, the sum of waits 150 + 250 × n ≤ 3,000 and the next poll would pass it); `The_parking_brake_keeps_its_fixed_settle` (the parking brake path waits `ReadBackDelayMs`, 1,500, as now). Existing tests that asserted one 1,500 ms wait after a plain write are updated to the poll; any that pass only because of the fixed wait are named in the commit message.
- [ ] **Step 2:** Run the filter `Bbs757FoWriteTests`: the new tests FAIL.
- [ ] **Step 3: Implement.** After the writes, `pollForLanding` true: wait `FoSettleMs`, read fresh; then every `FoPollMs` read again; landed when two reads in a row satisfy `LandedOnPick(control, after, target)`; stop when landed or when the waits would pass `FoLandMaxMs`; the result is the last verdict. `pollForLanding` false: today's `ReadBackDelayMs` and one read. The linked status read (`LinkedStatusKey`) follows the landing as now. `FoSetParkingBrakeCoreAsync` passes `pollForLanding: false`. `FoPressCoreAsync` is unchanged. Comment: why two reads (the REALISTIC parking brake writes itself back; evidence flight-10.md).
- [ ] **Step 4:** Run the filter `FirstOfficer.BBS757`: PASS.
- [ ] **Step 5:** Commit `perf(b757): the First Officer moves on once a switch has landed`.

### Task 5: The ILS read from BlueBird's variable

**Files:**
- Create: `MSFSBlindAssist/SimConnect/BBS757/Bbs757IlsVariableDisplay.cs` (the `IBbs757IlsDisplay` interface, moved here, and its variable reader)
- Modify: `MSFSBlindAssist/Aircraft/BBS757/Bbs757IlsPanel.cs` (`Bbs757IlsReading` moved here; `ReadingFrom`), `MSFSBlindAssist/Aircraft/BBS757/Bbs757NavRad.cs` (the new keys and `NavVariables`), `MSFSBlindAssist/Aircraft/BBS757Definition.Radios.cs` (registration, `IlsDisplayReader`, `RetireIlsDisplay`, `ResetRadios`, comments)
- Delete: `MSFSBlindAssist/Aircraft/BBS757/Bbs757IlsOcr.cs`, `MSFSBlindAssist/SimConnect/BBS757/CoherentBbs757IlsDisplay.cs`, `tests/MSFSBlindAssist.Tests/Bbs757IlsOcrTests.cs`, `tests/MSFSBlindAssist.Tests/Fixtures/bbs757-ils/`
- Create: `tests/MSFSBlindAssist.Tests/Bbs757IlsVariableDisplayTests.cs` (with `FakeIlsDisplay`, moved here without `Forgotten`)
- Test: `tests/MSFSBlindAssist.Tests/Bbs757IlsPanelTests.cs`, `Bbs757NavRadTests.cs`

**Interfaces:**
- Produces: `Bbs757NavRad.IlsFrequencyKey = "BBS757_ILS_FREQ"` (L:var `BBS_757_ILS_FREQ`), `VorLFrequencyKey = "BBS757_VOR_L_FREQ"` (`BBS_757_VOR_L_FREQ`), `VorRFrequencyKey = "BBS757_VOR_R_FREQ"` (`BBS_757_VOR_R_FREQ`), `VorLAutoKey = "BBS757_VOR_L_AUTO"` (`B757_MCP_VOR1_MODE_AUTO`), `VorRAutoKey = "BBS757_VOR_R_AUTO"` (`B757_MCP_VOR2_MODE_AUTO`); `Bbs757NavRad.NavVariables`, `IReadOnlyList<(string Key, string LVar, string Label)>` with those five (labels "ILS Frequency", "VOR L Frequency", "VOR R Frequency", "VOR L Auto Tuning", "VOR R Auto Tuning"), registered like `CourseVariables` (OnRequest, no Ctrl+M row, never shadowing). `Bbs757IlsPanel.ParkedValue = -1`; `static Bbs757IlsReading? Bbs757IlsPanel.ReadingFrom(double? frequency, double? course)`. `IBbs757IlsDisplay` keeps only `Task<Bbs757IlsReading?> ReadAsync(CancellationToken ct)` and `IDisposable`. `sealed class Bbs757IlsVariableDisplay(Func<string, CancellationToken, Task<double?>> readFresh) : IBbs757IlsDisplay`.

- [ ] **Step 1: Tests.** In `Bbs757IlsPanelTests`: `Reading_from_the_variable` (Theory: -1 and 95 → parked, course 95; 108.1 → 108.10; 108.15 → 108.15; 111.95 → 111.95; 110.5 and course null → 110.50, course null); `Reading_from_an_absent_variable_is_none` (0, null, 107.95, 112.0, 110.52 (no ILS channel), NaN → null); in `Bbs757IlsVariableDisplayTests`: the reader reads `IlsFrequencyKey` and `IlsCourseKey` fresh and answers `ReadingFrom`; a read that throws `OperationCanceledException` answers null. In `Bbs757NavRadTests`: N with the display answering 110.50 says "ILS 110.50." under `sv-SE`; with the display answering null (the variable absent) N says the count's words ("ILS parked by count." for a fresh load). The `SetIlsAsync` tests in `Bbs757IlsPanelTests` keep passing with `FakeIlsDisplay` (only the moved type).
- [ ] **Step 2:** Run the filters `Bbs757IlsPanelTests|Bbs757NavRadTests|Bbs757IlsVariableDisplayTests`: FAIL (missing types).
- [ ] **Step 3: Implement.** Move `Bbs757IlsReading` into `Bbs757IlsPanel.cs`; write `ReadingFrom` (parked within 0.001 of -1; a frequency through `TryDialFor`; a course rounded to whole degrees 0-359, else null); write `Bbs757IlsVariableDisplay`; register `NavVariables`; `IlsDisplayReader` answers `IlsDisplayFor?.Invoke(sim)` in tests, else, connected and not retired, `new Bbs757IlsVariableDisplay((key, ct) => WriteIoFor(sim).ReadFresh(key, ct))`; `RetireIlsDisplay` only sets the retired flag; `ResetRadios` loses its `ForgetGauge` line; delete the picture reader, its decoder, tests and fixtures. The spoken words are unchanged ("ILS tuning unknown: the ILS display could not be read." stays: the variable is what the display shows). Rewrite the comments that describe the picture (the Radios.cs file summary, the "read from a picture" section heading, `Bbs757IlsPanel`'s summary) to say the display is read from `BBS_757_ILS_FREQ` (-1 parked; added by BlueBird's 2026-10-31 update, not in its public table; flight-19.md).
- [ ] **Step 4:** Run the filter `Bbs757`: PASS; build the solution (`dotnet build MSFSBlindAssist.sln -c Debug`): no reference to a deleted type remains.
- [ ] **Step 5:** Commit `feat(b757): the ILS display is read from BlueBird's own variable, not a picture`.

### Task 6: N says the VORs from the variables when the CDU page cannot be read

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/BBS757/Bbs757NavRad.cs` (`TuningFromVariables`, `Readout`), `MSFSBlindAssist/Aircraft/BBS757Definition.Radios.cs` (`NavRadiosReadoutAsync`)
- Test: `tests/MSFSBlindAssist.Tests/Bbs757NavRadTests.cs`

**Interfaces:**
- Consumes: Task 5's `VorLFrequencyKey`, `VorRFrequencyKey`, `VorLAutoKey`, `VorRAutoKey`.
- Produces: `static Bbs757VorTuning? Bbs757NavRad.TuningFromVariables(double? mhz, double? auto)`: null when `mhz` is unread; `Mhz` null ("not tuned") when outside 108.00-117.95; else the frequency to two decimals; `Mode` 'A' for an auto flag of 1, 'M' for 0, null unread; `Ident` null. `Readout(Bbs757NavRadPage? page, int? vorL, int? vorR, int? ils, Bbs757IlsDial? ilsDial, Bbs757IlsReading? ilsRead = null, Bbs757VorTuning? vorLVar = null, Bbs757VorTuning? vorRVar = null)`.

- [ ] **Step 1: Tests.** `Without_the_page_the_vors_come_from_the_variables` (page null, vorLVar 116.05 auto, vorRVar 113.90 manual, VOR R course 95: "VOR L 116.05 auto. VOR R 113.90 manual, course 095. ILS course 095. ILS 110.50.", also under `sv-SE`); `Variables_out_of_the_vor_band_are_not_tuned` (0 and 118.0 → "VOR L not tuned"); `Without_the_page_or_the_variables_the_words_are_as_before` (both null: today's "… VOR frequencies unavailable." sentence, unchanged); `With_the_page_the_variables_are_not_used` (the page's "VOR L 116.90 auto, GSU." wins). Definition level (the window rig): with the CDU page unavailable N reads the four keys fresh and speaks the variables' sentence; with the page, it does not read them.
- [ ] **Step 2:** Run the filter `Bbs757NavRadTests`: FAIL.
- [ ] **Step 3: Implement** `TuningFromVariables` and the `Readout` branch (page null and either variable tuning known: each side's `VorPhrase` from its variable tuning, a side whose variable tuning is null saying `TuningPhrase`'s "frequency unavailable"); in `NavRadiosReadoutAsync`, when the screen is null, read the four keys through the same `io.Read` and pass them. With the page nothing changes (the owner keeps the station name).
- [ ] **Step 4:** Run the filter `Bbs757`: PASS.
- [ ] **Step 5:** Commit `feat(b757): N says the VORs from BlueBird's variables when the first officer's CDU page cannot be read`.

### Task 7: First Officer: every exterior light at its flow

**Files:**
- Modify: `MSFSBlindAssist/FirstOfficer/BBS757/Bbs757FoFlowDefinitions.cs`, `Bbs757FoChecklistDefinitions.cs`, `Bbs757FoStateEvaluator.cs`
- Test: `tests/MSFSBlindAssist.Tests/FirstOfficer/BBS757/Bbs757FoFlowTests.cs`, `Bbs757FoChecklistStructureTests.cs`, `Bbs757FoStateEvaluatorTests.cs`

**Interfaces:**
- Produces: evaluator fields `FO_GROUND_LIGHTS_OFF` (both runway turnoff lights and the taxi light read off) and `FO_GROUND_LIGHTS_ON` (all three on); `FO_PREFLIGHT_LIGHTS_OFF` gains `K.TaxiLight`. The taxi light is `K.TaxiLight` (`B757_OVHD_btn_lights_taxi`, BlueBird's one nose light), the logo `K.LogoLights`.

The decision (sources: BlueBird User Procedures v1.01 (UP) lines, IFALPA leaflet 09AGEBL01, AeroSavvy; flight-19.md):

| Flow | New step id, label, writes | Skip when | Checklist line (group, id, same label) | Source |
|---|---|---|---|---|
| Preflight | `PF_LIGHTS_OFF` keeps its id, label "Anti-collision, wing, landing, runway turnoff and taxi lights: OFF", adds `(K.TaxiLight, 0)` | `FO_PREFLIGHT_LIGHTS_OFF` | PREFLIGHT `PF_LIGHTS_OFF`, label and action the same | UP 3096, 3212-3216 |
| Before Start | `BS_LOGO` "Logo lights: ON", `K.LogoLights` 1, right after `BS_BEACON` | `IsOn(K.LogoLights)` | BEFORE_START `BS_LOGO`, Auto on `K.LogoLights` | IFALPA |
| After Start | `AS_TAXI_LIGHT` "Taxi light: ON", `K.TaxiLight` 1, after `AS_AUTOBRAKE` | `IsOn(K.TaxiLight)` | AFTER_START `AS_TAXI_LIGHT` | IFALPA; UP 7305 |
| Before Takeoff | `BT_TAXI_LIGHT` "Taxi light: ON", after `BT_TURNOFF` | `IsOn(K.TaxiLight)` | BEFORE_TAKEOFF `BT_TAXI_LIGHT` | UP 5862 |
| After Takeoff | `AT_GROUND_LIGHTS` "Runway turnoff and taxi lights: OFF", both turnoffs and the taxi light 0, right after `AT_GEAR_OFF` (before the flaps wait; the gear check stays last, FO-13) | `FO_GROUND_LIGHTS_OFF` | AFTER_TAKEOFF `AT_GROUND_LIGHTS`, before `AT_FLAPS` | UP 6328 |
| Before Landing | `BL_LIGHTS` "Taxi light and runway turnoff lights: ON", all three 1, after `BL_SPEEDBRAKE` | `FO_GROUND_LIGHTS_ON` | BEFORE_LANDING `BL_LIGHTS`, after `BL_SPEEDBRAKE` | UP 6588-6589 |
| After Landing | `AL_TURNOFF` "Runway turnoff lights: OFF" (both 0) and `AL_TAXI_LIGHT` "Taxi light: ON", both after `AL_WING_LIGHTS` | `FO_TURNOFF_LIGHTS_OFF`; `IsOn(K.TaxiLight)` | AFTER_LANDING `AL_TURNOFF`, `AL_TAXI_LIGHT` | UP 7304-7305 |

Unchanged: Power Up position ON; Before Takeoff landing, strobe, wing, turnoff ON; the 10,000 ft automation (FO-19); Descent landing ON; After Landing landing, strobe, wing OFF; Shutdown beacon, turnoff, wing, logo, taxi OFF with position ON. Each new step is `Done(Skip(SW or Multi …), lineId)` in the flows' own style; each new line is the checklist's `Auto(…)` with the same writes as its `CheckAction`; no read-back (`*_CL`) group changes.

- [ ] **Step 1: Tests.** Flow tests: each flow's step ids in order include the new ones where the table puts them (After Takeoff's last step is still `AT_CHK_GEAR`; Before Landing's last still `BL_CHK_GEAR`); each new step's writes and skip as in the table; checklist structure: each new line exists in its group with the step's label and is delivered by its step (the existing FO-6/FO-7 sweep tests cover the link once the line exists); evaluator: `FO_GROUND_LIGHTS_OFF`/`_ON` true, false and NaN (any of the three unread) cases; `FO_PREFLIGHT_LIGHTS_OFF` false with only the taxi light on.
- [ ] **Step 2:** Run the filter `FirstOfficer.BBS757`: FAIL.
- [ ] **Step 3: Implement** the table: flows, lines, fields. Update each touched flow's `Description` to name its lights.
- [ ] **Step 4:** Run the filter `FirstOfficer`: PASS (the cross-profile FO tests included).
- [ ] **Step 5:** Commit `feat(b757): the First Officer sets the taxi, runway turnoff and logo lights at their flows`.

### Task 8: First Officer: BlueBird's preflight tests

**Files:**
- Create: `MSFSBlindAssist/FirstOfficer/BBS757/Bbs757FoTests.cs` (the test table, pure)
- Modify: `MSFSBlindAssist/FirstOfficer/BBS757/Bbs757FoActionExecutor.cs` (`IBbs757FoTransport`, `DispatchAsync`), `Bbs757DefinitionFoTransport.cs`, `Bbs757FoFlowDefinitions.cs`, `Bbs757FoChecklistDefinitions.cs` (an `ActionManual` builder, the FBW A320's), `MSFSBlindAssist/Aircraft/BBS757Definition.FirstOfficer.cs` (`FoPressAsync` hold override, `FoSpringTestAsync`), `MSFSBlindAssist/Aircraft/BBS757Definition.Overhead.cs` (`KeepReleasingAsync` takes a nullable announcer)
- Test: `tests/MSFSBlindAssist.Tests/FirstOfficer/BBS757/Bbs757FoExecutorTests.cs`, `Bbs757FoFlowTests.cs`, `Bbs757FoChecklistStructureTests.cs`, `Bbs757FoWriteTests.cs`, `FakeBbs757FoTransport.cs`

**Interfaces:**
- Produces: `enum Bbs757FoTestKind { HeldButton, P61Switch }`; `sealed record Bbs757FoTest(string Key, string Label, string Target, Bbs757FoTestKind Kind, int? HoldMs, int AfterMs)`; `static class Bbs757FoTests { IReadOnlyList<Bbs757FoTest> Preflight; Bbs757FoTest? For(string key); }`. Transport: `Task<bool> PressHeldAsync(string node, int? holdMs, CancellationToken ct)` and `Task<bool> P61TestAsync(string buttonKey, CancellationToken ct)`. Definition: `FoPressAsync(SimConnectManager sim, string node, CancellationToken ct, int? holdMs = null)` (the hold replaces the map's when given) and `Task<bool> FoSpringTestAsync(SimConnectManager sim, string buttonKey, CancellationToken ct)` with its testable core `FoSpringTestCoreAsync(Bbs757SpringButton button, Bbs757SimIo io, CancellationToken ct)`: false for a button that is not a P61 test (`ReadoutKey` not null), a write that cannot land, or a switch already holding (`_springHolds`); else `HoldSpringAsync` (its waits on `CancellationToken.None`: a stop never cuts a hold short), the background release when it did not rest (`KeepReleasingAsync` with no announcer: silent), true when it rested. Silent throughout.

The tests, in BlueBird's order (User Procedures v1.01, simplified procedures pp. 36-42), keys `FO_TEST_*`:

| Key | Label (spoken as the step) | Target | Kind | Hold | After |
|---|---|---|---|---|---|
| `FO_TEST_SQUIB` | Squib test — the nine green lamps light | `B757_P6_SQUIBS_BUTTON2` | HeldButton | map (3 s) | 1 s |
| `FO_TEST_GND_PROX` | Ground proximity test — listen for the callouts | `BBS757_P61_GROUND_PROX_TEST` | P61Switch | 3 s | 15 s |
| `FO_TEST_STALL_L` | Left stall warning test — listen for the stick shaker | `BBS757_P61_LEFT_STALL_TEST` | P61Switch | 3 s | 1 s |
| `FO_TEST_STALL_R` | Right stall warning test — listen for the stick shaker | `BBS757_P61_RIGHT_STALL_TEST` | P61Switch | 3 s | 1 s |
| `FO_TEST_CONFIG_TO` | Takeoff config test — listen for the configuration warning | `BBS757_P61_TAKEOFF_CONFIG_TEST` | P61Switch | 3 s | 2 s |
| `FO_TEST_CONFIG_LDG` | Landing config test — listen for the configuration warning | `BBS757_P61_LANDING_CONFIG_TEST` | P61Switch | 3 s | 2 s |
| `FO_TEST_CVR` | CVR test | `B757_OVHD_SW_CVR_TEST` | HeldButton | map (3 s) | 1 s |
| `FO_TEST_OXY_CAPT` | Captain oxygen test — listen for the oxygen flow | `OXY_CAPT_TEST_SWITCH001` | HeldButton | 10 s | 1 s |
| `FO_TEST_OXY_FO` | First officer oxygen test — listen for the oxygen flow | `OXY_FO_TEST_SWITCH1` | HeldButton | 10 s | 1 s |
| `FO_TEST_TCAS` | TCAS test — listen for TCAS test pass | `B757_PED_TCAS_MODE_KNOB_TEST_CLICK` | HeldButton | map (3 s) | 8 s |
| `FO_TEST_FIRE_WHLWELL` | Wheel well fire test — listen for the fire bell | `B757_PED_FIRE_WHLWELL_BUTTON` | HeldButton | map (3 s) | 3 s |
| `FO_TEST_FIRE_ENG` | Engine fire test — listen for the fire bell | `B757_PED_FIRE_ENG_BUTTON` | HeldButton | map (3 s) | 3 s |
| `FO_TEST_FIRE_CARGO` | Engine, APU and cargo fire test — listen for the fire bell | `B757_PED_FIRE_CARGO_BUTTON` | HeldButton | map (3 s) | 3 s |

The After waits are first values, to be measured in the sim (Task 10's checks): each covers the aircraft's own sound and the cockpit watcher's announcements, which keep speaking during the tests (owner, 2026-11-05). No battery test (owner). Each test's line in the PREFLIGHT group, id = the step id `PF_` + the key's tail (`PF_TEST_SQUIB` …), is `ActionManual` with the same label and the executor key as its action (a self-test has no state: FO-12's NaN rule fires every tick). The `PF_TESTS` reminder step and line go.

- [ ] **Step 1: Tests.** `Bbs757FoTests`: the table is the thirteen above in that order, each `Target` a map node (HeldButton, a momentary or held control of the map) or a P61 test button key (`Bbs757SpringControls.ForButtonKey(..)` with no readout), keys unique. Executor (`FakeBbs757FoTransport` records `PressHeld node hold` and `P61Test key` and the delays): each key dispatches to its kind with its hold, then waits its After; a false press returns false and skips the wait; `A_stop_during_a_test_still_releases_it` (the stop fires during the hold: the transport's press still completes its release; for the P61 switch, the definition-level core's last write is the release). Definition (`Bbs757FoWriteTests`): `FoPressCoreAsync` with a hold of 10,000 waits 10,000 between down and up; `FoSpringTestCoreAsync` refuses a trim button, writes the hold every 30 ms for 3 s and the release, says nothing (no announcer involved), and returns true when the switch rests. Flows: Preflight's steps from `PF_TEST_SQUIB` to `PF_TEST_FIRE_CARGO` sit where `PF_TESTS` was, in table order, each `Done`-linked to its line; no `PF_TESTS` remains. Checklist: thirteen `ActionManual` lines in PREFLIGHT in the same order.
- [ ] **Step 2:** Run the filter `FirstOfficer.BBS757|Bbs757FoWriteTests`: FAIL.
- [ ] **Step 3: Implement** the table, the executor branch (`Bbs757FoTests.For(key)` checked before `io.Knows`; the press, then `io.Delay(test.AfterMs, stop)` only when it went out), the transport methods, the definition's hold override and silent P61 test, the flow steps (`Done(SW(id, label, key, 1), lineId)`, failure policy Skip: a test that could not be pressed is skipped aloud, never stops the flow) and the lines; the Preflight `Description` names the tests.
- [ ] **Step 4:** Run the filter `FirstOfficer|Bbs757`: PASS.
- [ ] **Step 5:** Commit `feat(b757): the First Officer runs BlueBird's preflight tests`.

### Task 9: Handoff to plan 16 and the record

- [ ] **Step 1:** Append to this file an **Execution record** (who, what changed from the plan and why, the suite count) and a **Handoff to plan 16** with these pilot-facing texts for its owned files, in their voice:
  - `docs/b757.md` "The autopilot window (Ctrl+P)": the button list ends with Speed Intervene, A/T Disconnect and A/P Disconnect (the captain's yoke button; a second press silences the warning), the disengage bar only on the MCP panel; the Radios section (plan 04's handoff) says the ILS display is read from `BBS_757_ILS_FREQ` and N's VOR fallback; the EFB section's side-menu rule; the First Officer section's tests, lights and pace.
  - `BBS757_Hotkeys.txt` Ctrl+P: "… LOC, APP, Speed Intervene, A/T Disconnect, A/P Disconnect (Alt+B: the yoke's button; press it again to silence the warning)".
  - The guide (`guide/boeing-aircraft-and-the-md-11.md`, 757 section, the Ctrl+P paragraph; `guide/first-officer.md`, the 757 row: the tests and when each light is set) — `User guide: needed: boeing-aircraft-and-the-md-11, first-officer` in the PR.
  - Candidate rule for `.claude/rules/b757.md`: "[B757-22] Read the 757's ILS frequency from `L:BBS_757_ILS_FREQ` (-1 parked) through `Bbs757IlsVariableDisplay`; never from a picture of the panel (the owner, 2026-11-05)."
- [ ] **Step 2:** Commit `docs(b757): plan 19's record and handoff`.

### Task 10 [controller]: Review, verify, push

- [ ] **Step 1:** Whole-branch review of plan 19's commits (superpowers:requesting-code-review), fixes as a wave, each commit with the trailers.
- [ ] **Step 2:** `dotnet build MSFSBlindAssist.sln -c Debug` and the full suite: 0 failed; record the count.
- [ ] **Step 3:** The in-sim checks for the owner (a few, prioritised): (1) Ctrl+P A/P Disconnect with CMD engaged in flight: CMD lights out, the warning, a second press silences; (2) the EFB at night: Aircraft and Navigation sections each read with their name, no "did not show"; (3) Ctrl+N: an ILS frequency set and parked, N's words; (4) a cold-and-dark Preflight flow: the thirteen tests in order, each heard (and the After waits judged), then Before Start to After Start for the logo and taxi lights and the pace; (5) a 2020 tester reads the ILS once (the variable on MSFS 2020).
- [ ] **Step 4:** Fetch `kn4iee/B757`; if it moved, rebase onto it and rerun the suite; then `git push kn4iee B757` (plain, no force).

## Execution record

Date: 2026-11-05. Worked by Claude with subagents (one implementer and one reviewer per task), on branch B757 in the main folder (the owner's go-ahead; no worktree). Base 6ad94a4ad. Every commit carries `Plan: 19`. Not yet pushed (Task 10).

- Task 0, c6c3f37ec: the plan and its evidence (`docs/b757/evidence/flight-19.md`).
- Task 1, 96dbf46a3: Ctrl+P's last button is A/P Disconnect, the captain's yoke button (a held press, a second press silences the warning); the disengage bar stays only on the MCP panel.
- Task 2, 4f7de825f: the EFB finds the selected side-menu entry on a dim tablet by its gap to the other bands (`SelectedEntry`); 16 dim fixtures pin it.
- Task 3, 1b9a3e07c: the EFB says "did not show" only when the tablet shows another page, not when only the section was not read.
- Task 4, aeac9dc86: the First Officer's switch writes poll (150 ms, then 250 ms steps, at most 3 s) and move on once two reads show the target; the parking brake keeps its fixed 1.5 s.
- Task 5, 4bb67cd2d: the ILS display is read from `L:BBS_757_ILS_FREQ` and its course variable through `Bbs757IlsVariableDisplay`; the picture reader (`Bbs757IlsOcr`, the Coherent display, 14 fixture images) is deleted.
- Task 6, 5e59cfcf0 and e960904be: N, and the Ctrl+N window, say the VORs from the four variables when the first officer's CDU page cannot be read.
- Task 7, a2448143d: the First Officer sets the taxi, runway turnoff and logo lights at their flows.
- Task 8, b72e14cf6 and 7e66a7eb3: the First Officer runs BlueBird's thirteen preflight tests (squib, ground proximity, left and right stall, takeoff and landing config, CVR, two oxygen, TCAS, three fire tests) in the Preflight flow, each with its checklist line.
- Task 9: this record.

### What changed from the plan, and why

- Task 1: `PressAsync` (Interaction.cs, not in the plan's file list) gained an optional `label` parameter. The map's label is "Captain Yoke A/P Disconnect", and the plan requires exactly "A/P Disconnect pressed".
- Task 2: three assertions in `The_page_behind_a_question_is_text_while_the_answers_and_the_top_bar_press` (bright/21-defuel-question) were updated: "Fuel" became "Fuel (current page)", "Total fuel: Import from SIMBRIEF" became "Import from SIMBRIEF", and "14%" became "Center tank: 14%". The old rule missed that page's band (gap 11.2 against 12), so the assertions recorded the reading without its section; raising the minimum gap above 11.2 would break the dim pages (10.7 to 13.6).
- Task 6: the Ctrl+N window keeps the same fallback as N, and two window-level tests pin it. The plan said the window's fill is unchanged; the design meant the VOR source stays the CDU page, and splitting the two would make the window say "VOR frequencies unavailable" while N gives the frequencies.
- Task 8: every test line carries a `LeaveAlone` guard (`NotParked`, as FO-25's lines are): a hand tick runs a test only on the ground with the engines stopped, and an unread state counts as not safe. The plan's line spec left the tick guard out; without it a tick could run a test in flight or during an engine start and hold the executor gate for up to about 18 s.
- Task 8: the P61 rows' hold is the switch's own 3 s (`Bbs757SpringControls.TestHoldMs`); the table records `HoldMs` null for them, since the P61 test takes no hold.

Final full suite, after Task 8: 21,078 passed, 0 failed.

### Open items

Deferred minors, for the final review:

- Task 1: `SayUnavailable` (Autopilot.cs:120) uses the map label ("Captain Yoke A/P Disconnect unavailable") while the press says "A/P Disconnect pressed".
- Task 1: "A/P Disconnect pressed" comes after the 1.5 s hold, so nothing is said meanwhile.
- Task 2: the bridge's "no menu entry selected" line prints empty bands on the SYSTEMS path; also require `MenuBands.Count > 0`.
- Task 2: `SelectedEntry` needs 3 or more bands, so a 2-entry menu cannot select (none exists among the fixtures).
- Task 2: no near-threshold test (gap just under 6, ratio exactly 3).
- Task 3: the `app:` branch of `ShowsOtherPage` has no test; an empty `pressedOn.App` should mean "no basis to compare"; no direct matrix test of `ShowsOtherPage`.
- Task 4: `FoLandMaxMs` is documented as the most a write waits, but the real ceiling is 2,900 ms; `A_target_read_once_then_lost_is_not_landed` asserts only `Sum() <= 3000`.
- Task 5: the two catches in `Bbs757IlsVariableDisplay` have identical bodies; a course of 360 reads null; one over-long summary line in `AddRadioVariables`.
- Task 6: the VOR band check is a strict float compare (compare rounded to 0.01); `TuningFromVariables(NaN)` gives "not tuned", not unread; `WindowRig`'s constructor doc sits above the new `Reads` field.
- Task 7: no direct assertion that Before Landing's `BL_LIGHTS` follows `BL_SPEEDBRAKE`, nor a step-level NaN skip test for `BL_LIGHTS` (the theories cover both).
- Task 8: a failed test step is announced "Skipping: <label with the listen-for cue>", also for a P61 test that ran but did not rest (its switch is released silently for up to 60 s); a tick whose test could not be pressed stays ticked silently; held-button tests take no claim, so a panel press of the same oxygen button during the 10 s hold lets go at 3 s; a tick's After wait runs on `CancellationToken.None`.

Only the sim can confirm:

- The After waits of the 13 tests (first values, judged by ear; the cockpit watcher keeps speaking during them).
- The oxygen tests' 10 s hold.
- The ILS variable on MSFS 2020.
- The live Ctrl+N ILS check: an ILS frequency set and parked through Ctrl+N, and N's words, read from `BBS_757_ILS_FREQ`.
- The frequency variable of a VOR that is not tuned.
- The A/P Disconnect press, and the silence of its warning on the second press.
- The EFB at night (Aircraft and Navigation sections each read with their name).
- Task 7's light steps and the pace of the flows with the polling writes.

## Handoff to plan 16

Plan 16 owns `docs/b757.md`, `BBS757_Hotkeys.txt`, `guide/**` and `.claude/rules/b757.md`; plan 19 touched none of them. Paste these in each file's voice.

- `docs/b757.md`:
  - "The autopilot window (Ctrl+P)": the button list ends with Speed Intervene, A/T Disconnect and A/P Disconnect. A/P Disconnect is the captain's yoke button (a held press; a second press silences the warning). The disengage bar is only on the MCP panel.
  - Radios section (plan 04's handoff): the ILS display is read from `L:BBS_757_ILS_FREQ` (-1 when parked) and its course variable, not from a picture of the panel. N's VOR words fall back to the four VOR variables when the first officer's CDU page cannot be read, and the Ctrl+N window does the same.
  - EFB section, side-menu rule: the selected entry is the one whose band stands out from the others (a gap of at least 6 and at least three times the second largest); when only the section was not read, a menu press says nothing, and "did not show" is spoken only when the tablet shows another page or app.
  - First Officer section, pace: a switch write moves on as soon as two reads show it landed (at most 3 s); the parking brake keeps its 1.5 s settle.
  - First Officer section, lights: Preflight turns anti-collision, wing, landing, both turnoff and taxi lights off; Before Start sets the logo lights; After Start sets the taxi light; Before Takeoff sets taxi, landing, turnoff, strobe and wing; After Takeoff turns the turnoff and taxi lights off; Before Landing turns the taxi and turnoff lights on; After Landing turns the landing, strobe and wing lights off (unchanged), the runway turnoff lights off and the taxi light on.
  - First Officer section, tests: the Preflight flow runs BlueBird's thirteen tests in order: squib, ground proximity, left stall, right stall, takeoff config, landing config, CVR, captain oxygen (held 10 s), first officer oxygen (held 10 s), TCAS, wheel well fire, engine fire, engine/APU/cargo fire. Each says what to listen for and has a checklist line; a hand tick runs it only parked with the engines stopped. There is no battery test.
- `BBS757_Hotkeys.txt`, Ctrl+P: "... LOC, APP, Speed Intervene, A/T Disconnect, A/P Disconnect (Alt+B: the yoke's button; press it again to silence the warning)".
- Guide: `guide/boeing-aircraft-and-the-md-11.md`, 757 section, the Ctrl+P paragraph (the last buttons and the yoke's A/P Disconnect); `guide/first-officer.md`, the 757 row (the thirteen tests and when each light is set). PR line: `User guide: needed: boeing-aircraft-and-the-md-11, first-officer`.
- Candidate rule for `.claude/rules/b757.md`: "[B757-22] Read the 757's ILS frequency from `L:BBS_757_ILS_FREQ` (-1 parked) through `Bbs757IlsVariableDisplay`; never from a picture of the panel (the owner, 2026-11-05)."
