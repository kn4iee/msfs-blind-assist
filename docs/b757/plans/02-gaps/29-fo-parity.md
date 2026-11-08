# BlueBird 757 First Officer: Tests, False Failures, Cabin Calls, ILS, Approach, and the Other Boeings' Automation (plan 29, added 2026-11-08)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0 and Task 10 are the controller's own.

**Goal:** The owner's First Officer requests of 2026-11-08: the preflight tests shortest first without the squib and landing configuration tests; no "Unable to complete" over an APU switched off; the parking brake set only with hydraulic pressure; cabin calls; an ILS reminder; and what the PMDG 737, iFly 737 and PMDG 777 First Officers have that the 757's lacks (an Approach flow, two Captain reminders, the 737s' APU cool-down, the LNAV and VNAV push at 400 ft, the center fuel pump automation).

**Architecture:** Table and flow changes in `Bbs757FoTests`, `Bbs757FoFlowDefinitions`, `Bbs757FoChecklistDefinitions` and `Bbs757FoKeys`; one synthetic retired in `Bbs757FoStateEvaluator`; the empty `Bbs757FoAutoManager` gains the 737s' push (reusing `LnavVnavPushHeight`) and the shared `CenterFuelPumpAutomation`; the executor gains an MCP push that waits for its light and the shared `CenterPumpGate` at its single chokepoint.

**Tech Stack:** C# 13 / .NET 10 Windows Forms, xUnit, SimConnect through MobiFlight calculator writes.

**Spec:** the owner's design of 2026-11-08, approved that day, kept locally (`docs/superpowers/specs/2026-11-08-b757-fo-parity-design.md`, git-ignored by the owner's choice); every decision and value a task needs is copied below. Rules: `.claude/rules/first-officer.md`, `first-officer-boeing.md`, `first-officer-automation.md`, `b757.md` (they load when you Read the code).

## Global Constraints

- Branch `B757` in the main folder (no worktree, as plan 19). Every commit carries the trailers `Plan: 29` and `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Nothing is pushed until Task 10.
- Tests: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --artifacts-path bin/b757-p29 --filter "FullyQualifiedName~<name>"` (the artifacts path spares the app's exe); the full suite the same without `--filter`. Never the bare csproj without `-p:Platform=x64` (CORE-1).
- Test names keep `Bbs757`. No test reads a file under `docs/b757/`.
- FO-5 (the 2 s pause) untouched. FO-6/FO-7: every new write step is `Done(...)`-linked to a line of its own group, and every new actionable line has a delivering step (`Bbs757FoFlowTests.EveryActionGroup_ListsTheSameLinesInTheSameOrderAsItsFlow` and `EveryActionGroupLine_IsDeliveredOnceByItsFlow` must keep passing). FO-12: a line with no state (test, chime) fires on every tick. FO-13: each gear check stays the last step of its flow. FO-14: no landing autobrake. FO-16: the push height is fixed. FO-26: step lines through `AnnounceProgress` (the flow manager does it).
- CORE-7: the First Officer's MCP press takes its light's change as its own echo (`LinkedStatusKey`), so the First Officer says the result once. Any new spoken number is invariant and tested under `sv-SE` (`TestCulture.Run`).
- File ownership (`00-index.md`): plan 29 takes `FirstOfficer/BBS757/**`, `Aircraft/BBS757Definition.FirstOfficer.cs`, `tests/.../FirstOfficer/BBS757/**`, and makes two small shared edits the design names: `FirstOfficer/CenterPumpGate.cs` (the 757's two keys join the list) and `Forms/Settings/FirstOfficerPanel.cs` (the center pump checkbox's text and description). The guide, `docs/b757.md` and rule lines stay plan 16's: Task 9 writes them as handoff requests.

## Review Focus

1. An APU selector read OFF while the APU still runs (its cool-down): "APU: OFF" reads done; the battery still stays on until `B757_APU_STATE` reads 0 — Task 3, `Bbs757FoStateEvaluatorTests.Apu_off_is_the_selector_even_while_the_apu_cools_down` and the existing `Secure_RemindsTheGpu_AndGatesTheBatteryOnTheApuAndExternalPower`.
2. The hydraulic pumps unread (NaN) at a parking brake tick: refused with the words, nothing sent — Task 4, `Bbs757FoChecklistStructureTests.Parking_brake_is_left_alone_without_brake_pressure` (an unread row).
3. The LNAV or VNAV light unread at 400 ft: nothing pressed, the leg's attempt not spent (FOB-21) — Task 7, `Bbs757FoAutoManagerTests.An_unread_light_presses_nothing_and_retries`.
4. BlueBird's fuel density unread: the center pound figure uses 6.7 lb/gal, never NaN, so a loaded center tank still turns its pumps on — Task 8, `Bbs757FoStateEvaluatorTests.Center_fuel_pounds_fall_back_to_6_7_per_gallon`.
5. The window opened in the climb above 400 ft: no push (only a crossing the manager watched) — Task 7, `Bbs757FoAutoManagerTests.A_window_opened_above_400_feet_never_pushes`.

---

### Task 0 [controller]: The plan

- [ ] **Step 1:** Commit this plan: `docs(b757): plan 29, the First Officer's parity with the other Boeings` with the trailers.

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

**Interfaces:** Produces `K.CabinCall = "B757_OVHD_FWD_CABIN_CALL_BUTTON"` (a momentary map button: the executor presses it). Steps and lines `BT_CABIN` "Cabin call: advise the cabin crew for takeoff" (before `BT_CONFIG`), `DS_CABIN` "Cabin call: landing preparation" (right after `DS_SEAT_BELTS`), `DS_ILS` Captain "ILS" with the reminder text "Set the ILS frequency and course in the NAV radios window, Input Ctrl+N." (right after `DS_AUTOBRAKE`; the owner dropped the audio ident once the plan had landed, 2026-11-08).

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

- [ ] **Step 1:** Fill in this plan's "Handoff to plan 16" and "Execution record" (the sweep table, commits, the suite count, what changed from the plan). Commit `docs(b757): plan 29's execution record`.

### Task 10 [controller]: Review, verify, push

- [ ] **Step 1:** Whole-branch review of plan 29's commits by a fresh reviewer; fixes as a wave, each with the trailers.
- [ ] **Step 2:** `dotnet build MSFSBlindAssist.sln -c Debug -p:Platform=x64` (MSFSBA closed, or the build is reported locked) and the full suite: 0 failed; record the count.
- [ ] **Step 3:** The owner's EFB layout file (owner, 2026-11-08): after the Debug build, check that the 757 tablet layout is where the Debug app reads it (`%APPDATA%\MSFSBlindAssist\layouts\bbs757`, plan 22's v3 layout); if it is missing, say so and how it gets there, never a guess.
- [ ] **Step 4:** Fetch `kn4iee/B757`; if it moved, rebase onto it and rerun the suite; then `git push kn4iee B757` (plain, no force).

## In-sim checks (owner)

1. **APU off:** After Start, tick "APU: OFF" by hand with the APU running: it stays ticked. Final Securing with the APU running: "Waiting 120 seconds: APU cool-down", the wait for the APU to stop, then the battery. Note how long the APU takes to stop after the 2 minutes.
2. **Cold and dark, Before Start flow:** the hydraulic pumps, then the parking brake sets. With the brake released and the pumps off, tick "Parking brake: SET": the refusal words.
3. **Departure:** the eleven preflight tests in order; the takeoff cabin chime; LNAV and VNAV pushed at 400 ft when not armed ("400 feet. LNAV and VNAV on.", heard once: no second "LNAV on" from the light's own announcement); with the setting on and about 1,500 lb in the center tank, the center pumps go off when it runs dry.
4. **Arrival:** the Descent chime and ILS reminder; the Approach flow (ND APP 20, the two reminders); "Set the missed approach altitude" in Before Landing.

## Execution record

- **Claimed by:** Claude, for the owner (kn4iee), 2026-11-08. Written as plan 23; renumbered 29 before its push, when Robin's plans 23-28 (the displays) reached `B757` first (the index: a claim counts once it is on the remote).
- **Status:** landed on `B757`, waiting on the owner's in-sim checks (above).
- Worked inline by Claude (superpowers:executing-plans, TDD per task, one fresh whole-branch reviewer at the end), on `B757` in the main folder. Base c9989d1c9 (`kn4iee/B757` after the rebase; this plan f70f4f43d). Every commit carries `Plan: 29`.
- Task 2, d86c0b645: eleven preflight tests, shortest first, no squib or landing config test.
- Task 3, be4ebe5cd: "APU: OFF" reads the APU switch; Final Securing's 2-minute cool-down (`SC_APU_COOLDOWN`).
- Task 4, c34044c46: Before Start turns the electric hydraulic pumps on before the parking brake; the brake is left alone without brake pressure.
- Task 5, 51f0dab83: the FWD cabin call in Before Takeoff and Descent; Descent's ILS reminder.
- Task 6, a599a6fd9: the Approach flow and group (13 flows, 22 groups); Preflight's ND MAP 40; the missed approach and MCP reminders.
- Task 7, 8804ce066: LNAV and VNAV at 400 ft (`Bbs757FoAutoManager`, `PushMcpAsync`).
- Task 8, 092907890: the center fuel pumps (`FO_CENTER_FUEL_LBS`, the shared gate, the policy, the settings text).
- Task 9, 78ec8d635 (the record); the review's fix, 600684bf8; the renumbering, e3d3b6929.

### What changed from the plan, and why

- Task 6: `BeforeStart_WaitsForTheIrsAlignment_LastBeforeTheCaptainsItems` now expects the IRS check right before `BS_MCP`, not `BS_ACARS`: the MCP reminder is a Captain item, and the IRS check stays last before them.
- Task 7: the push's words are queued (`Announce`), as the 757's phase monitor speaks its crossings; the PMDG 737 interrupts.
- Task 8: the center pump words are said once the write lands ("did not switch on/off" when it did not); the 737s speak before writing. The manager takes an optional clock so the policy's 2 s window is testable.

### Sweep (Task 1): can a write that worked be reported failed?

A hand tick says "Unable to complete" when its line's state still reads false 10 s after its action and the
executor's queue (FO-9, FO-10); a flow write says "Skipping: …" when its write returns false. A plain switch write
(`FoSetCoreAsync`) returns true once two reads in a row show the target, within 3 s; the window polls every First
Officer control each second, and a write's end requests a fresh read.

| Lines (hand tick) and their flow steps | State read | What the write waits for | Verdict |
|---|---|---|---|
| PU_BATTERY, PU_STBY_POWER, PU_POSITION, PF_EMER_LTS (guarded), PF_IGNITION, PF_WIPERS, PF_NO_SMOKING, PF_PRESS_MODE, PF_ND_MODE, PF_ND_RANGE, PF_BANK_LIMIT, PF_AUTOBRAKE, BS_SEAT_BELTS, BS_APU_BLEED, BS_XPDR, BS_BEACON, BS_LOGO, AS_APU_BLEED, AS_AUTOBRAKE, AS_TAXI_LIGHT, BT_SEAT_BELTS, BT_STROBES, BT_WING, BT_TAXI_LIGHT, DS_SEAT_BELTS, AP_ND_MODE, AP_ND_RANGE, AL_AUTOBRAKE, AL_WING_ANTI_ICE, AL_STROBES, AL_WING_LIGHTS, AL_TAXI_LIGHT, SD_BEACON, SD_SEAT_BELTS, SD_WING_ANTI_ICE, SD_WIPERS, SD_APU_BLEED, SC_EMER_LTS, SC_BATTERY | the switch it writes | two reads of the target, 3 s | no false failure |
| PU_FUEL_CUTOFF, PU_ELEC_HYD_OFF, PU_ELEC_PANEL, PU_IRS (two clicks a switch), PF_ENGINE_CONTROLS (guarded EECs on a RR), PF_YAW_DAMPERS, PF_ENG_HYD, PF_START_SEL, PF_XFEED, PF_FUEL_PUMPS, PF_ANTI_ICE, PF_LIGHTS_OFF, PF_WINDOW_HEAT, PF_TRIM_AIR, PF_ISOLATION, PF_BLEEDS, PF_FDS, BS_HYD, BS_FUEL (center ON gated only over an empty tank, which the line then wants OFF), ES_PACKS_OFF, ES_BLEEDS, ES_ISOLATION, ES_E1_FUEL, ES_E2_FUEL, AS_PACKS, AS_ISOLATION, BT_LANDING, BT_TURNOFF, AT_GROUND_LIGHTS, BL_LIGHTS, AL_WXR_TERR, AL_LANDING, AL_TURNOFF, AL_FDS, SD_ENG_ANTI_ICE, SD_FUEL_CUTOFF, SD_ELEC_HYD, SD_FUEL_PUMPS, SD_WINDOW_HEAT, SD_ISOLATION, SD_FDS, SD_HEATERS, SD_TURNOFF, SD_EXT_LIGHTS, SD_XPDR, SC_IRS | a synthetic of exactly the switches it writes | each switch as above, in turn | no false failure |
| AS_APU, SC_APU | `FO_APU_OFF`: the APU switch (Task 3) | the switch, 3 s | fixed by Task 3 (was the APU's running state: BlueBird's cool-down outlasted the grace) |
| BS_APU, AL_APU | `FO_APU_RUNNING` (`B757_APU_STATE` 2) | the action holds until the APU runs, up to 150 s (FO-11; measured 58-80 s) | no false failure |
| PU_EXT_PWR, BS_EXT_PWR_OFF, SC_EXT_PWR | the EXT PWR ON light (status row) | the light, up to 4.5 s | no false failure. A tick of "External power: ON" with no GPU sends nothing and says "Unable to complete": true, but without the flow's reason (the flow leaves it alone with words). Not changed: not a false failure. |
| PF_AT_ARM | BlueBird's A/T armed flag (R40) | the flag, up to 3 s | the tick: no false failure (a later flag ticks the line within the grace). The flow step could say "Skipping" if the flag lagged past 3 s: never observed, so not changed (watch in the in-sim checks). |
| AS_SPEEDBRAKE, BL_SPEEDBRAKE, AL_SPEEDBRAKE | the lever (streamed row) | the lever, up to 3.5 s | no false failure |
| BS_PARK, SD_PARK | `L:B757_FC_PARK_BRAKE` | the lever toggle, then full braking (REALISTIC), one read each after 1.5 s | the one logged failure (2026-11-07) was real (no hydraulic pressure); fixed by Task 4. A brief 1-0-1 write-back after a set lasts about 2 s (logged 2026-11-06 and 11-07), inside the grace. |
| ES_E1_GND, ES_E2_GND | N2 past the starter | the selector, 3 s | never reverts (StayComplete, FOB-1) |
| PF_TEST_* (eleven), BT_CABIN, DS_CABIN | none (no state) | the press sent | never reverts; a flow step fails only when nothing was sent |
| The lines with no action (PU_GEAR, BS_DOORS, AS_START_SEL, BT_ENGINES, AT_FLAPS, AT_GEAR, BL_GEAR) and every reminder | — | — | nothing to report |

### Final review (Task 10)

A fresh whole-branch reviewer: no Critical; one Important, fixed: the 400 ft push said "LNAV did not come on" when it had
pressed nothing (the fresh read found the light lit or unread), which could send the pilot to toggle a lit mode off.
`PushMcpAsync` now returns `McpPush` (not pressed, pressed and lit, pressed and not lit), and only a press that went out
is spoken (`A_light_found_lit_on_the_fresh_read_is_neither_pressed_nor_reported`, RED then GREEN). The handoff's
docs and rule-homing gaps it named are added below.

Deferred minors (not changed): the gate's ON test is `target is 1` (an untargeted center pump write, which no caller
makes, would pass ungated; the PMDG form is `(target ?? 1) == 1`); a light lighting more than 1.5 s after the press could
be said twice ("heard once" added to in-sim check 3); `LnavVnavPushHeight`'s summary still says "both 737s" (handoff).
Set aside, each ruled: FlowManager's `WaitSeconds` does not hold on Pause (shared, as the 737s' cool-downs);
`FO_APU_RUNNING` reads the cooling APU as running, so a Before Start run within 2 minutes of a hand switch-off would skip
the APU start (pre-existing, a false success rather than a false failure; for the owner to judge).

Final full suite, after the fix: 21,669 passed, 0 failed.

## Handoff to plan 16

**What changes for a pilot** (for the pull request and the changelog, category `improvement` with one `fix` line):
- The First Officer's preflight tests run quickest first, ground proximity last; the squib and landing configuration tests are gone (both can still be pressed on their panels).
- Ticking "APU: OFF" no longer says "Unable to complete" while the APU cools down (fix). Final Securing waits a 2-minute APU cool-down after switching the APU off, as on the 737s.
- Before Start turns the electric hydraulic pumps on before setting the parking brake. Without hydraulic pressure the First Officer leaves the brake alone and says "Parking brake left released: no hydraulic pressure. Turn on the electric hydraulic pumps first."
- The First Officer rings the cabin chime before takeoff and for landing preparation (Descent), and reminds you to set the ILS frequency and course.
- A new Approach flow (the ND to APP at 20 NM, the altimeters, LOC then APP); Preflight puts the ND back to MAP at 40 NM; Before Landing reminds the missed approach altitude, Before Start the MCP speed, heading and altitude.
- With "Auto-engage autopilot on climbout" on, the First Officer presses LNAV and VNAV at 400 ft if they are not lit: "400 feet. LNAV and VNAV on."
- With "Auto-manage center fuel pumps" on (now for the BlueBird 757 too), the center pumps go on in ground setup with center fuel and off when the center tank runs dry: "Center tank low. Center fuel pumps off."

**The guide** (`guide/**`): the 757 First Officer text (the tests, the cabin calls, the ILS reminder, the Approach flow, the parking brake words, the two automations); `guide/first-officer.md` line 136, the center pump setting row, names the BlueBird 757 (the app's checkbox now reads "Auto-manage center fuel pumps (PMDG 737/777, iFly MAX8 and BlueBird 757)"). `docs/b757.md`'s First Officer section the same. The "User guide:" line.

**`docs/first-officer.md`:** its section "Center fuel pump auto-management (PMDG 737 + 777 + iFly MAX8)" and its text on
the 400 ft LNAV/VNAV push (today the 737s' only) name the BlueBird 757 too. The full texts of FO-16 (the fixed push
height) and FOB-21 (the latch burn) name the 757's `Bbs757FoAutoManager` beside the 737s. The doc comment of
`LnavVnavPushHeight` ("for both 737s") names the 757, which reuses it from the `PMDG737` namespace.

**Noticed in passing (2026-11-08), for the release:** the private layouts store's `layouts` branch still holds the 757's
generator-version-2 file (`bbs757/39462c4a…-49c0a29a1e5b8cf1.json`) beside the v3 one plan 22 published
(`…-69911a5bb8c57b24.json`). Under plan 22's format the v2 file no longer loads ("missing required properties including
'keyboardShown'"), and `LayoutBundler` is all-or-nothing, so once this branch is on `main` the release and preview
workflows fail at the bundle step (UPD-10) until the v2 file is removed from the store. This plan's Debug build was
bundled from a copy of the store without it.

**Proposed rule lines** (`docs/invariants/b757.md` and the rule files). The center pump line must load on the 757's
First Officer files: `first-officer-boeing.md`'s globs do not reach `FirstOfficer/BBS757/**`, so it is homed in
`b757.md` (or mirrored there word for word), or `ClaudeContextBudgetTests` fails on it.
- [B757-n] The 757 First Officer's "APU: OFF" lines and steps read the APU switch (`FO_APU_OFF`), never `B757_APU_STATE`: BlueBird runs the APU through a cool-down after OFF. Final Securing waits `ApuCooldownSeconds` (120) only after a switch-off it sent, then for the state to read 0; the battery waits for 0 (`BatteryMustStay`).
- [B757-n] Never send the 757 parking brake SET without brake pressure (`Bbs757FoChecklistDefinitions.NoBrakePressure`: the left or right electric hydraulic pump ON, or an engine running); never turn a pump on to set it.
- [B757-n] (homed in `b757.md`, see above) The BlueBird 757's center-pump ON gate sits at `Bbs757FoActionExecutor.DispatchAsync`, its single chokepoint (`CenterPumpGate`, the 757 map's `B757_OVHD_C_PUMPS_L/R_BUTTON`), the tank in pounds (`FO_CENTER_FUEL_LBS`, 6.7 lb/gal until BlueBird's density reads), as FOB-10/FOB-18. FOB-16's exemption, written down: the 757 passes an unread tank as NaN, not through `SafeRoundToInt`, which is safe because the manager's `dataReady` needs every input and the policy rejects NaN (as the iFly does).
- [B757-n] The 757's 400 ft push says only what a press did (`McpPush`): a light found lit or unread on the fresh read is not pressed and not reported, never "did not come on" over a lit mode (a pilot would toggle it off).
