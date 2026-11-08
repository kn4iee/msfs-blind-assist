# BlueBird 757 First Officer: Tests, False Failures, Cabin Calls, ILS, Approach, and the Other Boeings' Automation (plan 23, added 2026-11-08)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0 and Task 10 are the controller's own.

**Goal:** The owner's First Officer requests of 2026-11-08: the preflight tests shortest first without the squib and landing configuration tests; no "Unable to complete" over an APU switched off; the parking brake set only with hydraulic pressure; cabin calls; an ILS reminder; and what the PMDG 737, iFly 737 and PMDG 777 First Officers have that the 757's lacks (an Approach flow, two Captain reminders, the 737s' APU cool-down, the LNAV and VNAV push at 400 ft, the center fuel pump automation).

**Architecture:** Table and flow changes in `Bbs757FoTests`, `Bbs757FoFlowDefinitions`, `Bbs757FoChecklistDefinitions` and `Bbs757FoKeys`; one synthetic retired in `Bbs757FoStateEvaluator`; the empty `Bbs757FoAutoManager` gains the 737s' push (reusing `LnavVnavPushHeight`) and the shared `CenterFuelPumpAutomation`; the executor gains an MCP push that waits for its light and the shared `CenterPumpGate` at its single chokepoint.

**Tech Stack:** C# 13 / .NET 10 Windows Forms, xUnit, SimConnect through MobiFlight calculator writes.

**Spec:** the owner's design of 2026-11-08, approved that day, kept locally (`docs/superpowers/specs/2026-11-08-b757-fo-parity-design.md`, git-ignored by the owner's choice); every decision and value a task needs is copied below. Rules: `.claude/rules/first-officer.md`, `first-officer-boeing.md`, `first-officer-automation.md`, `b757.md` (they load when you Read the code).

## Global Constraints

- Branch `B757` in the main folder (no worktree, as plan 19). Every commit carries the trailers `Plan: 23` and `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Nothing is pushed until Task 10.
- Tests: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --artifacts-path bin/b757-p23 --filter "FullyQualifiedName~<name>"` (the artifacts path spares the app's exe); the full suite the same without `--filter`. Never the bare csproj without `-p:Platform=x64` (CORE-1).
- Test names keep `Bbs757`. No test reads a file under `docs/b757/`.
- FO-5 (the 2 s pause) untouched. FO-6/FO-7: every new write step is `Done(...)`-linked to a line of its own group, and every new actionable line has a delivering step (`Bbs757FoFlowTests.EveryActionGroup_ListsTheSameLinesInTheSameOrderAsItsFlow` and `EveryActionGroupLine_IsDeliveredOnceByItsFlow` must keep passing). FO-12: a line with no state (test, chime) fires on every tick. FO-13: each gear check stays the last step of its flow. FO-14: no landing autobrake. FO-16: the push height is fixed. FO-26: step lines through `AnnounceProgress` (the flow manager does it).
- CORE-7: the First Officer's MCP press takes its light's change as its own echo (`LinkedStatusKey`), so the First Officer says the result once. Any new spoken number is invariant and tested under `sv-SE` (`TestCulture.Run`).
- File ownership (`00-index.md`): plan 23 takes `FirstOfficer/BBS757/**`, `Aircraft/BBS757Definition.FirstOfficer.cs`, `tests/.../FirstOfficer/BBS757/**`, and makes two small shared edits the design names: `FirstOfficer/CenterPumpGate.cs` (the 757's two keys join the list) and `Forms/Settings/FirstOfficerPanel.cs` (the center pump checkbox's text and description). The guide, `docs/b757.md` and rule lines stay plan 16's: Task 9 writes them as handoff requests.

## Review Focus

1. An APU selector read OFF while the APU still runs (its cool-down): "APU: OFF" reads done; the battery still stays on until `B757_APU_STATE` reads 0 — Task 3, `Bbs757FoStateEvaluatorTests.Apu_off_is_the_selector_even_while_the_apu_cools_down` and the existing `Secure_RemindsTheGpu_AndGatesTheBatteryOnTheApuAndExternalPower`.
2. The hydraulic pumps unread (NaN) at a parking brake tick: refused with the words, nothing sent — Task 4, `Bbs757FoChecklistStructureTests.Parking_brake_is_left_alone_without_brake_pressure` (an unread row).
3. The LNAV or VNAV light unread at 400 ft: nothing pressed, the leg's attempt not spent (FOB-21) — Task 7, `Bbs757FoAutoManagerTests.An_unread_light_presses_nothing_and_retries`.
4. BlueBird's fuel density unread: the center pound figure uses 6.7 lb/gal, never NaN, so a loaded center tank still turns its pumps on — Task 8, `Bbs757FoStateEvaluatorTests.Center_fuel_pounds_fall_back_to_6_7_per_gallon`.
5. The window opened in the climb above 400 ft: no push (only a crossing the manager watched) — Task 7, `Bbs757FoAutoManagerTests.A_window_opened_above_400_feet_never_pushes`.

---

### Task 0 [controller]: The plan

- [ ] **Step 1:** Commit this plan: `docs(b757): plan 23, the First Officer's parity with the other Boeings` with the trailers.

### Task 1: The written sweep for false failures

No code unless the sweep finds a line. **Files:** this plan's execution record ("Sweep" table).

- [ ] **Step 1:** For every line of `Bbs757FoChecklistDefinitions.Build()` with a `CheckAction` (a hand tick that can end in "Unable to complete", FO-9) and every flow write step of `Bbs757FoFlowDefinitions.Build()` (a failure says "Skipping: …"), write one row: id, state field, what its write waits for and how long (`FoLandMaxMs` 3 s and two reads for a plain switch; the executor's own waits for the FO_* keys), and the verdict: can a write that worked be reported failed? A line whose state can lag more than the 10 s grace (or a flow write whose check can miss a landed switch) is a finding.
- [ ] **Step 2:** Each finding gets a fix of the kind Task 3 uses (judged by what the pilot set, or a wait inside its action, FO-11) and a test, in this task, committed `fix(b757): <line> no longer reports a failure over a write that worked`. Expected from the design's first sweep: only `AS_APU` and `SC_APU`, which Task 3 fixes; the sweep re-checks the lines Tasks 2-6 add.

### Task 2: The preflight tests, shortest first

**Files:** Modify `FirstOfficer/BBS757/Bbs757FoTests.cs`, `FirstOfficer/BBS757/Bbs757FoFlowDefinitions.cs` (Preflight `Description`). Test: `tests/.../FirstOfficer/BBS757/Bbs757FoFlowTests.cs`, `Bbs757FoChecklistStructureTests.cs`, `Bbs757FoExecutorTests.cs`.

**Interfaces:** Produces `Bbs757FoTests.Preflight` with eleven entries in this order (keys unchanged): `FO_TEST_STALL_L`, `FO_TEST_STALL_R`, `FO_TEST_CVR`, `FO_TEST_CONFIG_TO`, `FO_TEST_FIRE_WHLWELL`, `FO_TEST_FIRE_ENG`, `FO_TEST_FIRE_CARGO`, `FO_TEST_OXY_CAPT`, `FO_TEST_OXY_FO`, `FO_TEST_TCAS`, `FO_TEST_GND_PROX`. `FO_TEST_SQUIB` and `FO_TEST_CONFIG_LDG` are gone.

- [ ] **Step 1: Tests.** `Bbs757FoFlowTests.PreflightTestIds` becomes the eleven `PF_TEST_*` ids in that order; `Preflight_RunsBlueBirdsTests_WhereTheReminderWas_EachLinkedToItsLine` finds the first test by `PreflightTestIds[0]`; add `Preflight_tests_run_shortest_first` (each test's hold plus after-wait, with a held button's map hold 3,000 ms and a P61 switch's `Bbs757SpringControls.TestHoldMs`, never decreases along the list) and `Preflight_has_no_squib_or_landing_config_test` (neither key in `Bbs757FoTests.Preflight`, neither id in the flow or the PREFLIGHT group). `Preflight_Description_NamesTheTests` drops "squib" and asserts the description does not contain "squib" or "landing configuration". Update the structure and executor tests that name the squib (`Bbs757FoChecklistStructureTests` line ~458 finds the first by `PreflightTestIds[0]`; `Bbs757FoExecutorTests`' table copy and its squib `InlineData` move to `FO_TEST_CVR`).
- [ ] **Step 2:** Run the filter `Bbs757Fo`: the new tests FAIL.
- [ ] **Step 3: Implement.** Reorder and trim the table; its summary says "shortest first (owner, 2026-11-08), the squib and landing configuration tests left out", not "in its order". The Preflight `Description` lists "both stall warnings, the takeoff configuration warning, CVR, the wheel well, engine, APU and cargo fire tests, both crew oxygen masks, TCAS and ground proximity".
- [ ] **Step 4:** Filter `Bbs757Fo`: PASS. **Step 5:** Commit `feat(b757): the First Officer's preflight tests run shortest first, without the squib and landing config tests`.

### Task 3: APU off by the selector, and the 737s' 2-minute cool-down

**Files:** Modify `Bbs757FoStateEvaluator.cs`, `Bbs757FoFlowDefinitions.cs` (After Start `AS_APU`, Secure), `Bbs757FoChecklistDefinitions.cs` (`AS_APU`, `SC_APU`). Test: `Bbs757FoStateEvaluatorTests.cs`, `Bbs757FoFlowTests.cs`, `Bbs757FoChecklistStructureTests.cs`.

**Interfaces:** Produces `FO_APU_OFF` = the APU selector (`K.ApuSelector`, `B757_OVHD_APU_SWITCH`) reading 0, NaN while unread; it no longer reads `B757_APU_STATE`. Secure's new step `SC_APU_COOLDOWN`: `WaitSeconds` 120, label "APU cool-down", `RunOnlyIfStepSent = "SC_APU"`.

- [ ] **Step 1: Tests.** `Bbs757FoStateEvaluatorTests.Apu_off_is_the_selector_even_while_the_apu_cools_down`: selector 0 with state 2 → 1; selector 0 with state 0 → 1; selector 1 with state 3 → 0; selector unread → NaN. `Bbs757FoFlowTests.Secure_cools_the_apu_down_two_minutes_after_switching_it_off`: order `SC_APU`, `SC_APU_COOLDOWN` (WaitSeconds 120, `RunOnlyIfStepSent` "SC_APU"), `SC_APU_STOPPED`, `SC_EXT_PWR`; `SC_APU`'s skip condition is true for selector 0 with state 2 and false for selector 1. `AfterStart_apu_off_is_already_set_by_the_selector`: `AS_APU`'s skip condition true for selector 0 with state 2. In `Bbs757FoChecklistStructureTests`, `AS_APU` and `SC_APU` read `FO_APU_OFF` (unchanged ids).
- [ ] **Step 2:** Run: FAIL. **Step 3: Implement.** `FO_APU_OFF` = `Vars(v => Pos(v[0], 0), K.ApuSelector)`. `SC_APU`'s skip becomes `s => s.IsOn("FO_APU_OFF")`; insert `RunOnlyIfSent(Done(Wait("SC_APU_COOLDOWN", "APU cool-down", 120), ...)` before `SC_APU_STOPPED` (add a `Wait(id, label, seconds)` builder: `FlowStepActionType.WaitSeconds`, `WaitSeconds`); `SC_APU_STOPPED` keeps its 150 s and Stop. The Secure `Description` names the 2-minute cool-down. The battery rule (`BatteryMustStay`, state 0) is unchanged. Comments cite the 737s (`PMDG737FlowDefinitions.BuildSecure`, the iFly's 120 s, FOB-22) and the owner's ruling.
- [ ] **Step 4:** Filter `Bbs757Fo`: PASS. **Step 5:** Commit `fix(b757): "APU: OFF" reads the APU switch, as on the 737s; Final Securing cools the APU down 2 minutes`.

### Task 4: The parking brake after the hydraulics, and only with brake pressure

**Files:** Modify `Bbs757FoFlowDefinitions.cs` (Before Start order, `BS_PARK`, `SD_PARK`), `Bbs757FoChecklistDefinitions.cs` (BEFORE_START order, `BS_PARK`, `SD_PARK`, the predicate and its text). Test: `Bbs757FoFlowTests.cs`, `Bbs757FoChecklistStructureTests.cs`.

**Interfaces:** Produces `Bbs757FoChecklistDefinitions.NoBrakePressure(Bbs757FoStateEvaluator s)`: true unless the left or right electric hydraulic pump (`K.LeftElecHyd`, `K.RightElecHyd`) reads ON, or an engine reads running (`FO_ENG1_RUNNING` or `FO_ENG2_RUNNING`); unread counts as no pressure. `BrakeStaysText = "Parking brake left released: no hydraulic pressure. Turn on the electric hydraulic pumps first."`

- [ ] **Step 1: Tests.** `Bbs757FoFlowTests.BeforeStart_sets_the_parking_brake_after_the_electric_hydraulic_pumps`: Before Start begins `BS_ON_GROUND`, `BS_CHK_DOORS`, `BS_HYD`, `BS_CHK_HYD`, `BS_PARK`, then `BS_WING_PUMPS`. `Bbs757FoChecklistStructureTests.Parking_brake_is_left_alone_without_brake_pressure` (rows: all unread → refused; R elec ON → allowed; L elec ON → allowed; only C1 and C2 ON → refused; engine 2 N2 at idle → allowed) for both `BS_PARK` and `SD_PARK`, the line's and the step's `LeaveAloneWhen`, the text exact.
- [ ] **Step 2:** Run: FAIL. **Step 3: Implement.** Move the step and its group line; `LeaveAlone(...)` on both lines and both steps (the step keeps its "Already set" skip first). The read-back `BSC_PARK` keeps its place.
- [ ] **Step 4:** PASS. **Step 5:** Commit `fix(b757): the First Officer sets the parking brake after the hydraulics, and never without brake pressure`.

### Task 5: Cabin calls and the ILS reminder

**Files:** Modify `Bbs757FoKeys.cs`, `Bbs757FoFlowDefinitions.cs` (Before Takeoff, Descent), `Bbs757FoChecklistDefinitions.cs`. Test: `Bbs757FoFlowTests.cs`, `Bbs757FoChecklistStructureTests.cs`.

**Interfaces:** Produces `K.CabinCall = "B757_OVHD_FWD_CABIN_CALL_BUTTON"` (a momentary map button: the executor presses it). Steps and lines `BT_CABIN` "Cabin call: advise the cabin crew for takeoff" (before `BT_CONFIG`), `DS_CABIN` "Cabin call: landing preparation" (right after `DS_SEAT_BELTS`), `DS_ILS` Captain "ILS" with the reminder text "Set and identify the ILS: frequency and course in the NAV radios window, Input Ctrl+N, and the ident on the audio panel." (right after `DS_AUTOBRAKE`).

- [ ] **Step 1: Tests.** `Bbs757FoFlowTests.BeforeTakeoff_and_Descent_call_the_cabin_with_the_fwd_button`: both steps `SetSwitch` on `K.CabinCall` target 1, Skip policy, no skip condition (no state), linked to their own line; their places as above. `Descent_reminds_the_ils_after_the_landing_autobrake`: `DS_ILS` is a Captain reminder with the exact text, after `DS_AUTOBRAKE`. In `Bbs757FoChecklistStructureTests`: `BT_CABIN` and `DS_CABIN` are action-only lines (`ActionManual`, no state field) whose action presses `K.CabinCall`; `DS_ILS` a reminder.
- [ ] **Step 2:** FAIL. **Step 3: Implement** with the existing builders (`SW`, `Captain`, `ActionManual`, `Reminder`). The two descriptions name the cabin call and, for Descent, the ILS.
- [ ] **Step 4:** PASS. **Step 5:** Commit `feat(b757): the First Officer calls the cabin before takeoff and for landing, and reminds the ILS`.

### Task 6: An Approach flow, the ND for departure, and two Captain reminders

**Files:** Modify `Bbs757FoKeys.cs`, `Bbs757FoStateEvaluator.cs` (poll fields), `Bbs757FoFlowDefinitions.cs`, `Bbs757FoChecklistDefinitions.cs`. Test: `Bbs757FoFlowTests.cs`, `Bbs757FoChecklistStructureTests.cs`, `FoDepartureGroupsTests` if it lists the 757's groups.

**Interfaces:** Produces `K.NdMode = "B757_ADI_1_CTR_KNOB"` (`NdModeApp = 1`, `NdModeMap = 2`), `K.NdRange = "B757_ADI_1_TFC_KNOB"` (`NdRange20 = 1`, `NdRange40 = 2`), both in `PollFields`. A new flow `APPROACH`, "Approach", between `DESCENT` and `BEFORE_LANDING`, related groups `APPROACH` (new action group, between `DESCENT_CL` and `APPROACH_CL` in `Build()`) and `APPROACH_CL`: `AP_ND_MODE` "Captain's ND mode: APP", `AP_ND_RANGE` "Captain's ND range: 20 NM" (each `Done(Skip(...))`, "Already set" on its position), `AP_ALTIMETERS` Captain "Altimeters: set to the local QNH.", `AP_LOC_APP` Captain "Arm LOC when cleared for the approach, then APP." Preflight gains `PF_ND_MODE` "Captain's ND mode: MAP" and `PF_ND_RANGE` "Captain's ND range: 40 NM" right after `PF_FDS`. Before Landing gains `BL_MISSED` Captain "Missed approach altitude" (reminder "Set the missed approach altitude.") right after `BL_LIGHTS`. Before Start gains `BS_MCP` Captain "MCP" (reminder "Set the MCP speed, heading and initial altitude.") right before `BS_ACARS`.

- [ ] **Step 1: Tests.** `FlowIds_InFlightOrder` gains `APPROACH` after `DESCENT`. New: `Approach_sets_the_nd_and_reminds_the_altimeters_and_loc` (the four steps in order, the writes `(K.NdMode, 1)` and `(K.NdRange, 1)`, their skip conditions true at those positions); `Preflight_sets_the_nd_to_map_40_after_the_flight_directors`; `BeforeLanding_reminds_the_missed_approach_before_the_checks_gear_still_last`; `BeforeStart_reminds_the_mcp_before_acars` (ACARS then the clearance stay last). The existing FO-6/FO-7 audits must pass with the new group.
- [ ] **Step 2:** FAIL. **Step 3: Implement.** The descriptions of Preflight, Approach, Before Landing and Before Start name their new items.
- [ ] **Step 4:** Filter `FirstOfficer`: PASS (the cross-profile tests too). **Step 5:** Commit `feat(b757): the First Officer's Approach flow, the ND for departure, and the missed approach and MCP reminders`.

### Task 7: LNAV and VNAV at 400 ft

**Files:** Modify `Bbs757FoAutoManager.cs`, `Bbs757FoProfile.cs` (`CreateAutoManager`), `Bbs757FoActionExecutor.cs`, `Bbs757FoKeys.cs`. Create `tests/.../FirstOfficer/BBS757/Bbs757FoAutoManagerTests.cs`.

**Interfaces:**
- `K.LnavButton = "B757_MCP_LNAV_BUTTON"`, `K.VnavButton = "B757_MCP_VNAV_BUTTON"`, `K.LnavLight = "BBS757_MCP_LIGHT_LNAV"`, `K.VnavLight = "BBS757_MCP_LIGHT_VNAV"` (streamed rows; `Bbs757McpLights`).
- `Bbs757FoActionExecutor.PushMcpAsync(string buttonNode, string lightKey) : Task<bool>`: on the gate, presses the button and reads the light fresh every 500 ms for up to 3,000 ms; true once it reads lit. Never pressed over a light read lit or unread (false).
- `Bbs757FoAutoManager(Bbs757FoActionExecutor executor, Bbs757FoStateEvaluator state, Action<string> say, Func<bool> autoApEnabled, Func<bool> centerPumpsEnabled)`. Its pure decision `internal static (bool PushLnav, bool PushVnav, bool BurnLatch) LnavVnavDecision(bool armed, bool spent, bool climbing, double aglFt, double lnavLight, double vnavLight)` reuses `LnavVnavPushHeight.Feet` and `ShouldBurnLatch`; arming per `LnavVnavPushHeight.Armed` (armed on the ground, re-armed at touchdown). Words: `"400 feet. LNAV and VNAV on."`, `"400 feet. LNAV on."`, `"400 feet. VNAV on."`; a push whose light did not come on: `"400 feet. LNAV did not come on."` (or VNAV, or "LNAV and VNAV did not come on").

- [ ] **Step 1: Tests** in `Bbs757FoAutoManagerTests` (pure decision and the manager with a fake transport): `Climbing_through_400_feet_pushes_the_unlit_modes`; `A_lit_light_is_not_pushed_and_spends_the_attempt`; `An_unread_light_presses_nothing_and_retries`; `A_window_opened_above_400_feet_never_pushes`; `The_setting_off_never_pushes`; `One_attempt_per_leg_re_armed_at_touchdown`; `The_words_say_on_or_did_not_come_on` (exact strings).
- [ ] **Step 2:** FAIL. **Step 3: Implement.** `Update` never blocks the SimConnect thread: the push runs as a task, its words spoken when it ends; a second push never starts while one runs. Gated on `FOAutoApEnabled` (read live through the delegate). The class summary drops "deliberately empty".
- [ ] **Step 4:** PASS. **Step 5:** Commit `feat(b757): the First Officer pushes LNAV and VNAV at 400 feet, as on the 737s`.

### Task 8: The center fuel pumps

**Files:** Modify `Bbs757FoStateEvaluator.cs`, `Bbs757FoActionExecutor.cs`, `Bbs757FoAutoManager.cs`, `Bbs757FoFlowDefinitions.cs` (`CenterPumpTarget`), `FirstOfficer/CenterPumpGate.cs`, `Forms/Settings/FirstOfficerPanel.cs`. Test: `Bbs757FoStateEvaluatorTests.cs`, `Bbs757FoExecutorTests.cs`, `Bbs757FoAutoManagerTests.cs`, the existing `CenterPumpGate` tests.

**Interfaces:**
- `Bbs757FoStateEvaluator.FallbackPoundsPerGallon = 6.7`; `internal static double CenterFuelLbs(double gallons, double poundsPerGallon)`: gallons × density (the fallback when the density is NaN or not above 0), NaN when gallons are NaN, via `FuelSystemLogic.SafeRoundToInt` where an int is needed (FOB-16). `FO_CENTER_FUEL_LBS` synthetic from `K.CenterFuelGallons` and `Bbs757Loadsheet.FuelDensityKey` (added to `PollFields`). `FO_CENTER_FUEL_FOR_PUMPS` becomes `FO_CENTER_FUEL_LBS > CenterFuelPumpAutomation.OffThresholdLbs`, and `FO_FUEL_PANEL_SET` the same test; `CenterFuelPumpsOnGallons` is retired.
- `CenterPumpGate.IsCenterOnEvent` also accepts `"B757_OVHD_C_PUMPS_L_BUTTON"` and `"B757_OVHD_C_PUMPS_R_BUTTON"`. The 757 executor's `DispatchAsync` returns true with nothing written for a center pump ON while `ShouldSuppressCenterOn(key, target is 1, FO_CENTER_FUEL_LBS)` (FOB-10/FOB-18), before anything else.
- `Bbs757FoActionExecutor.SetCenterFuelPumps(int on) : Task<bool>` (both center pumps). The auto manager feeds `CenterFuelPumpAutomation.Update(enabled, dataReady, onGround, centerQtyLbs, centerPumpsOn, wingPumpsOn, rawElapsedMs)` with the setting, "every input read", `FO_CENTER_FUEL_LBS`, either center pump ON, all four wing pumps ON, and a `Stopwatch`; TurnOn says "Center fuel pumps on.", TurnOff "Center tank low. Center fuel pumps off." (the 737s' words); it logs through `CenterPumpDiagnostics("BBS757")`.
- The settings checkbox text: `"Auto-manage center fuel pumps (PMDG 737/777, iFly MAX8 and BlueBird 757)"`; its description: `"On the PMDG 737 and 777, the iFly MAX8 and the BlueBird 757, turn the center tank fuel pumps on during ground setup when center fuel is loaded, and off when the center tank runs dry."`

- [ ] **Step 1: Tests.** `Center_fuel_pounds_fall_back_to_6_7_per_gallon` (gallons 300 with density unread → 2010; with 6.72 → 2016; gallons unread → NaN); `Center_pumps_for_start_follow_the_pound_threshold` (149 gal at 6.7 → off, 160 gal → on); `A_center_pump_on_over_an_empty_tank_is_suppressed_but_off_never_is` (executor, fake transport: nothing written, true returned; an OFF is written); the gate test gains the two 757 keys; `Bbs757FoAutoManagerTests.Center_pumps_turn_off_when_the_tank_runs_dry_and_say_so` and `The_center_pump_setting_off_does_nothing`.
- [ ] **Step 2:** FAIL. **Step 3: Implement.**
- [ ] **Step 4:** Filter `FirstOfficer` and `CenterPump`: PASS. **Step 5:** Commit `feat(b757): the First Officer manages the center fuel pumps, as on the other Boeings`.

### Task 9: Handoff to plan 16 and the record

- [ ] **Step 1:** Fill in this plan's "Handoff to plan 16" and "Execution record" (the sweep table, commits, the suite count, what changed from the plan). Commit `docs(b757): plan 23's execution record`.

### Task 10 [controller]: Review, verify, push

- [ ] **Step 1:** Whole-branch review of plan 23's commits by a fresh reviewer; fixes as a wave, each with the trailers.
- [ ] **Step 2:** `dotnet build MSFSBlindAssist.sln -c Debug -p:Platform=x64` (MSFSBA closed, or the build is reported locked) and the full suite: 0 failed; record the count.
- [ ] **Step 3:** The owner's EFB layout file (owner, 2026-11-08): after the Debug build, check that the 757 tablet layout is where the Debug app reads it (`%APPDATA%\MSFSBlindAssist\layouts\bbs757`, plan 22's v3 layout); if it is missing, say so and how it gets there, never a guess.
- [ ] **Step 4:** Fetch `kn4iee/B757`; if it moved, rebase onto it and rerun the suite; then `git push kn4iee B757` (plain, no force).

## In-sim checks (owner)

1. **APU off:** After Start, tick "APU: OFF" by hand with the APU running: it stays ticked. Final Securing with the APU running: "Waiting 120 seconds: APU cool-down", the wait for the APU to stop, then the battery. Note how long the APU takes to stop after the 2 minutes.
2. **Cold and dark, Before Start flow:** the hydraulic pumps, then the parking brake sets. With the brake released and the pumps off, tick "Parking brake: SET": the refusal words.
3. **Departure:** the eleven preflight tests in order; the takeoff cabin chime; LNAV and VNAV pushed at 400 ft when not armed ("400 feet. LNAV and VNAV on."); with the setting on and about 1,500 lb in the center tank, the center pumps go off when it runs dry.
4. **Arrival:** the Descent chime and ILS reminder; the Approach flow (ND APP 20, the two reminders); "Set the missed approach altitude" in Before Landing.

## Execution record

- **Claimed by:** Claude, for the owner (kn4iee), 2026-11-08.
- **Status:** in progress.

## Handoff to plan 16

(Task 9.)
